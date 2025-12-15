using Microsoft.AspNetCore.Authorization; // Necesario para [Authorize]
using Microsoft.AspNetCore.Mvc;
using Sale.Domain.Entities;
using Sale.Domain.Ports;
using System;
using System.Collections.Generic;
using System.Security.Claims; // Necesario para leer Claims
using System.Threading.Tasks;

namespace Sale.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SalesController : ControllerBase
    {
        private readonly ISaleService _saleService;

        public SalesController(ISaleService saleService)
        {
            _saleService = saleService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var sales = await _saleService.GetAllSalesAsync();
            return Ok(sales);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var sale = await _saleService.GetSaleByIdAsync(id);
            if (sale == null) return NotFound(new { message = "Venta no encontrada." });
            return Ok(sale);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateSaleRequest request)
        {
            // 1. OBTENER ID DEL USUARIO DESDE EL TOKEN (JWT)
            // Buscamos el claim "sub" (Subject) o "uid" o NameIdentifier, depende de cómo generes el token.
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value
                         ?? User.FindFirst("uid")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Token inválido: No contiene ID de usuario." });
            }

            var saleEntity = new SaleEntity
            {
                clientId = request.ClientId,
                created_by = userId // ✅ El ID viene del token, no del body (Seguro)
            };

            try
            {
                var createdSale = await _saleService.CreateSaleAsync(saleEntity, request.Items);
                return CreatedAtAction(nameof(GetById), new { id = createdSale.id }, createdSale);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error creando venta", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] SaleEntity sale)
        {
            if (id != sale.id) return BadRequest(new { message = "ID mismatch" });

            var userId = GetCurrentUserId(); // Usando método helper (abajo)

            // Asignar quién modificó
            sale.updated_by = userId;

            try
            {
                await _saleService.UpdateSaleAsync(sale);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error actualizando", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = GetCurrentUserId();

            try
            {
                await _saleService.SoftDeleteSaleAsync(id, userId);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error eliminando", error = ex.Message });
            }
        }

        // Helper privado para no repetir código
        private string GetCurrentUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value
                   ?? "unknown_user";
        }
    }

    // El Request ya NO pide UserId, porque lo sacamos del token
    public class CreateSaleRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public List<SaleItemPayload> Items { get; set; } = new();
    }
}