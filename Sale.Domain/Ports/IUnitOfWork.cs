using System;
using System.Data;
using System.Threading.Tasks;

namespace Sale.Domain.Ports
{
    public interface IUnitOfWork : IDisposable
    {
        Task BeginTransactionAsync();

        Task CommitAsync();

        Task RollbackAsync();
        Task EnsureConnectionOpenAsync();
        ISaleRepository SaleRepository { get; }
        IOutboxRepository OutboxRepository { get; }
    }
}