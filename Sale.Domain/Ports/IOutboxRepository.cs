using System.Collections.Generic;
using System.Threading.Tasks;
using Sale.Domain.Entities;

namespace Sale.Domain.Ports
{
    public interface IOutboxRepository
    {
        Task AddAsync(OutboxMessage message);
        Task<IEnumerable<OutboxMessage>> GetPendingAsync(int limit = 100);
        Task MarkSentAsync(string id);
        Task IncrementAttemptAsync(string id);
    }
}