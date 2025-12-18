using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sale.Domain.Entities
{
    public class SaleEntity
    {
        // UUID CHAR(36)
        public string id { get; set; } = string.Empty;
        public DateTime date { get; set; } = DateTime.Now;
        public decimal totalAmount { get; set; }
        public string clientId { get; set; } = string.Empty;
        public string status { get; set; } = "PENDING_DETAILS";
        public string? rejection_reason { get; set; }
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime? updated_at { get; set; }
        public string? created_by { get; set; }
        public string? updated_by { get; set; }
        public bool is_deleted { get; set; } = false;

        public SaleEntity()
        {
        }
    }
}
