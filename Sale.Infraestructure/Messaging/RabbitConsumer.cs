using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sale.Infraestructure.Data;
using Sale.Domain.Ports;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Sale.Infraestructure.Messaging
{
    public class RabbitConsumer : BackgroundService
    {
        private readonly IConnection _conn;
        private readonly IModel _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RabbitConsumer> _log;
        private readonly string _exchange;
        private readonly string _queueName = "sale.queue";

        public RabbitConsumer(IConfiguration cfg, IServiceScopeFactory scopeFactory, ILogger<RabbitConsumer> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;

            var factory = new ConnectionFactory
            {
                HostName = cfg["RabbitMQ:Host"] ?? "localhost",
                UserName = cfg["RabbitMQ:User"] ?? "guest", 
                Password = cfg["RabbitMQ:Password"] ?? "guest",
                DispatchConsumersAsync = true
            };

            _exchange = cfg["RabbitMQ:Exchange"] ?? "saga.exchange";
            _conn = factory.CreateConnection();
            _channel = _conn.CreateModel();
            _channel.ExchangeDeclare(_exchange, ExchangeType.Topic, durable: true);

            _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);

            // Bind the routing keys this service cares about
            _channel.QueueBind(_queueName, _exchange, "sale.details.persisted");
            _channel.QueueBind(_queueName, _exchange, "stock.reserved");
            _channel.QueueBind(_queueName, _exchange, "stock.reservation_failed");
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += OnReceived;
            // ahora el consumer usa ack manual
            _channel.BasicConsume(_queueName, autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }

        private async Task OnReceived(object sender, BasicDeliverEventArgs ea)
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            var routingKey = ea.RoutingKey;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // MessageId for idempotency if provided; otherwise compute a deterministic hash
                string messageId = root.TryGetProperty("MessageId", out var midProp) && midProp.GetString() is { } midStr && !string.IsNullOrEmpty(midStr)
                    ? midStr
                    : ComputeHash(routingKey, json);

                // uso de IIdempotencyStore resuelto por scope
                using var scope = _scopeFactory.CreateScope();
                var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
                var publisher = scope.ServiceProvider.GetService<IEventPublisher>();

                if (await idempotency.HasProcessedAsync(messageId))
                {
                    _log.LogInformation("Mensaje ya procesado: {msg} ({rk})", messageId, routingKey);
                    // ACK para eliminar de la cola
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                // Procesamiento por routing key
                if (routingKey == "sale.details.persisted")
                {
                    // payload espera: { "MessageId": "...", "sale_id": "V-100", "total_calculated": 50.00, ... }
                    var saleId = root.GetProperty("sale_id").GetString();
                    var total = root.GetProperty("total_calculated").GetDecimal();

                    await UpdateSaleTotalAsync(saleId, total);
                    await idempotency.MarkProcessedAsync(messageId, routingKey);
                    _log.LogInformation("Sale details persisted processed: {sale}", saleId);
                }
                else if (routingKey == "stock.reserved")
                {
                    // payload espera: { "MessageId": "...", "sale_id":"V-100", "total_calculated":50.00 }
                    var saleId = root.GetProperty("sale_id").GetString();
                    decimal? total = root.TryGetProperty("total_calculated", out var t) ? t.GetDecimal() : null;

                    await FinalizeSaleApprovedAsync(saleId, total);
                    await idempotency.MarkProcessedAsync(messageId, routingKey);

                    // publicar SaleCompleted
                    if (publisher != null)
                    {
                        var evt = new
                        {
                            MessageId = Guid.NewGuid().ToString(),
                            sale_id = saleId,
                            total_amount = total,
                            completed_at = DateTime.UtcNow
                        };
                        await publisher.PublishAsync("sale.completed", evt);
                    }

                    _log.LogInformation("Stock reserved processed and sale finalized: {sale}", saleId);
                }
                else if (routingKey == "stock.reservation_failed")
                {
                    var saleId = root.GetProperty("sale_id").GetString();
                    var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : "unknown";

                    await FinalizeSaleFailedAsync(saleId, reason);
                    await idempotency.MarkProcessedAsync(messageId, routingKey);

                    if (publisher != null)
                    {
                        var evt = new
                        {
                            MessageId = Guid.NewGuid().ToString(),
                            sale_id = saleId,
                            reason,
                            failed_at = DateTime.UtcNow
                        };
                        await publisher.PublishAsync("sale.failed", evt);
                    }

                    _log.LogWarning("Stock reservation failed for sale {sale}: {reason}", saleId, reason);
                }
                else
                {
                    _log.LogWarning("Routing key no manejada: {rk}", routingKey);
                }

                // Si todo OK, ack
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (JsonException jex)
            {
                _log.LogError(jex, "Mensaje JSON inválido routingKey={rk} body={b}", routingKey, json);
                // Malformed => ACK para descartar (no tiene sentido reintentar)
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error procesando mensaje routingKey={rk} body={b}", routingKey, json);
                // Error transitorio => NACK y requeue para reintento (puedes cambiar a false y enviar a DLX)
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        }

        private static string ComputeHash(string routingKey, string payload)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(routingKey + "|" + payload));
            return Convert.ToHexString(bytes);
        }

        private async Task UpdateSaleTotalAsync(string? saleId, decimal total)
        {
            if (string.IsNullOrEmpty(saleId)) return;
            const string sql = @"UPDATE sales SET total_amount = @total, updated_at = @u WHERE id = @id AND is_deleted = FALSE;";
            using var con = DatabaseConnection.Instance.GetConnection();
            await con.OpenAsync();
            using var cmd = new MySqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@total", total);
            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@id", saleId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task FinalizeSaleApprovedAsync(string? saleId, decimal? total)
        {
            if (string.IsNullOrEmpty(saleId)) return;
            const string sql = @"UPDATE sales SET total_amount = COALESCE(@total, total_amount), status = 'APPROVED', updated_at = @u WHERE id = @id AND is_deleted = FALSE;";
            using var con = DatabaseConnection.Instance.GetConnection();
            await con.OpenAsync();
            using var cmd = new MySqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@total", (object?)total ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@id", saleId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task FinalizeSaleFailedAsync(string? saleId, string reason)
        {
            if (string.IsNullOrEmpty(saleId)) return;
            const string sql = @"UPDATE sales SET status = 'REJECTED', updated_at = @u WHERE id = @id AND is_deleted = FALSE;";
            using var con = DatabaseConnection.Instance.GetConnection();
            await con.OpenAsync();
            using var cmd = new MySqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@id", saleId);
            await cmd.ExecuteNonQueryAsync();
        }

        public override void Dispose()
        {
            _channel?.Close();
            _conn?.Close();
            base.Dispose();
        }
    }
}