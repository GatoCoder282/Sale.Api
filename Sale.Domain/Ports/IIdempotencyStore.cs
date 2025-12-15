using System.Threading.Tasks;

namespace Sale.Domain.Ports
{
    public interface IIdempotencyStore
    {
        Task<bool> HasProcessedAsync(string messageId);
        Task MarkProcessedAsync(string messageId, string routingKey);
    }
}