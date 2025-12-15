using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sale.Domain.Ports;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sale.Infraestructure.Messaging
{
    public class OutboxProcessor : BackgroundService
    {
        private readonly IOutboxRepository _outbox;
        private readonly IEventPublisher _publisher;
        private readonly ILogger<OutboxProcessor> _log;
        private readonly int _batchSize;
        private readonly TimeSpan _interval;

        public OutboxProcessor(IOutboxRepository outbox, IEventPublisher publisher, ILogger<OutboxProcessor> log, Microsoft.Extensions.Configuration.IConfiguration cfg)
        {
            _outbox = outbox;
            _publisher = publisher;
            _log = log;
            _batchSize = int.TryParse(cfg["Outbox:BatchSize"], out var bs) ? bs : 50;
            _interval = TimeSpan.FromSeconds(int.TryParse(cfg["Outbox:IntervalSeconds"], out var s) ? s : 5);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pending = await _outbox.GetPendingAsync(_batchSize);
                    foreach (var msg in pending)
                    {
                        try
                        {
                            var payload = JsonSerializer.Deserialize<object>(msg.Payload) ?? msg.Payload;
                            await _publisher.PublishAsync(msg.RoutingKey, payload);
                            await _outbox.MarkSentAsync(msg.Id);
                            _log.LogInformation("Outbox message {id} published rk={rk}", msg.Id, msg.RoutingKey);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Error publicando outbox {id}", msg.Id);
                            await _outbox.IncrementAttemptAsync(msg.Id);
                        }

                        if (stoppingToken.IsCancellationRequested) break;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error procesando outbox");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}