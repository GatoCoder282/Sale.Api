using Sale.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sale.Domain.Ports // O Sale.Application.Services
{
    // Clase auxiliar para los items (ya que no usas DTOs completos)
    public class SaleItemPayload
    {
        public string MedId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public interface ISaleService
    {
        Task<SaleEntity> CreateSaleAsync(SaleEntity sale, List<SaleItemPayload> items);
        Task<IEnumerable<SaleEntity>> GetAllSalesAsync();
        Task<SaleEntity?> GetSaleByIdAsync(string id);
        Task UpdateSaleAsync(SaleEntity sale);
        Task SoftDeleteSaleAsync(string id, string userId);
    }
}