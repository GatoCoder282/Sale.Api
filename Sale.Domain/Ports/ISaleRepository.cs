using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sale.Domain.Entities;

namespace Sale.Domain.Ports
{
    public interface ISaleRepository
    {
        Task<SaleEntity> Create(SaleEntity sale);
        Task<SaleEntity?> GetById(SaleEntity sale);
        Task<IEnumerable<SaleEntity>> GetAll();
        Task Update(SaleEntity sale);
        Task Delete(SaleEntity sale);
    }
}
