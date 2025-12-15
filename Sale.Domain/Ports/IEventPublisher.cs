using System.Threading.Tasks;

namespace Sale.Domain.Ports
{
    public interface IEventPublisher
    {
        Task PublishAsync(string routingKey, object @event);
    }
}
