using Microsoft.EntityFrameworkCore;
using Prueba_tecnica.Contexto;
using Prueba_tecnica.Entidades;
using Prueba_tecnica.Entidades.Dto;
using System;
using System.Globalization;
using System.Text.Json;

namespace Prueba_tecnica.Servicio
{
    public interface IRecepcionService
    {
        Task<Recepcion> ProcesarRecepcionAsync(RecepcionDto request);
    }
    public class RecepcionService : IRecepcionService
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;

        public RecepcionService(AppDbContext context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<Recepcion> ProcesarRecepcionAsync(RecepcionDto request)
        {
            if (string.IsNullOrWhiteSpace(request.SKU))
                throw new Exception("SKU inválido");

            if (request.Cantidad <= 0)
                throw new Exception("Cantidad inválida");

            if (request.ValorDeclaradoUSD < 0)
                throw new Exception("Valor inválido");


            // TODO: 1. Validar existencia del Producto
            var producto = _context.Productos.FirstOrDefault(a => a.SKU==request.SKU);

            if (producto == null)
            {
                producto = new Producto { SKU = request.SKU, Nombre = request.NombreProducto };
                _context.Productos.Add(producto);
            }


            // TODO: 2. Validar que la capacidad de la Ubicación sea suficiente.
            var ubicacion = _context.Ubicaciones.FirstOrDefault(a => a.Id==request.UbicacionId) ?? throw new Exception($"Ubicación con id {request.UbicacionId} no existe");
            var capacidadDisponible = ubicacion.CapacidadMaxima - ubicacion.OcupacionActual;
            if(request.Cantidad> capacidadDisponible)
            {
                throw new Exception($"No hay capacidad suficiente en la ubicación con id {ubicacion.Id}");
            }

            // TODO: 3. Obtener cotización de moneda mediante API externa.
            decimal tasaDeCambio = 0;

            try
            {
                var cliente = _httpClientFactory.CreateClient();
                var response = await cliente.GetStringAsync("https://api.exchangerate-api.com/v4/latest/USD");
                using var doc = JsonDocument.Parse(response);

                tasaDeCambio = doc.RootElement.GetProperty("rates").GetProperty("ARS").GetDecimal();
            }
            catch (Exception)
            {
                //api de respaldo
                try
                {
                    var cliente = _httpClientFactory.CreateClient();
                    var response = await cliente.GetStringAsync("GET\r\nhttps://dolarapi.com/v1/dolares/blue");
                    using var doc = JsonDocument.Parse(response);

                    tasaDeCambio = doc.RootElement.GetProperty("venta").GetDecimal();
                }
                catch (Exception)
                {
                    //ambas apis fallaron, fallback por default para no detener el flujo
                    tasaDeCambio = 1400;
                }
            }


            // TODO: 4. Actualizar la ocupación de la Ubicación.
            ubicacion.OcupacionActual += request.Cantidad;

            // TODO: 5. Guardar el nuevo registro de Recepción en la base de datos y retornarlo.
            var recepcion = new Recepcion
            {
                ProductoId = producto.Id,
                UbicacionId = ubicacion.Id,
                Cantidad = request.Cantidad,
                FechaRecepcion = DateTime.Now,
                ValorDeclaradoUSD = request.ValorDeclaradoUSD,
                ValorMonedaLocal = request.ValorDeclaradoUSD * tasaDeCambio
            };
            _context.Recepciones.Add(recepcion);

            await _context.SaveChangesAsync();

            return recepcion;
        }
    }
}
