using MySql.Data.MySqlClient;
using Sale.Domain.Entities;
using Sale.Domain.Ports;
using Sale.Infraestructure.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Sale.Infraestructure.Persistence
{
    public class OutboxRepository : IOutboxRepository
    {
        private readonly MySqlConnection _connection;
        private readonly MySqlTransaction? _transaction;

        public OutboxRepository(MySqlConnection connection, MySqlTransaction? transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public async Task AddAsync(OutboxMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Id))
                message.Id = Guid.NewGuid().ToString();

            const string sql = @"
INSERT INTO outbox (id, aggregate_id, routing_key, payload, status, created_at, attempt_count, error_log)
VALUES (@id, @aggregate_id, @rk, @payload, @status, @created_at, @attempts, @error);";

            var opened = _transaction == null && _connection.State != ConnectionState.Open;
            if (opened) await _connection.OpenAsync();
            try
            {
                using var cmd = new MySqlCommand(sql, _connection, _transaction);
                cmd.Parameters.AddWithValue("@id", message.Id);
                cmd.Parameters.AddWithValue("@aggregate_id", message.AggregateId ?? string.Empty);
                cmd.Parameters.AddWithValue("@rk", message.RoutingKey);
                cmd.Parameters.AddWithValue("@payload", message.Payload);
                cmd.Parameters.AddWithValue("@status", message.Status);
                cmd.Parameters.AddWithValue("@created_at", message.CreatedAt);
                cmd.Parameters.AddWithValue("@attempts", message.AttemptCount);
                cmd.Parameters.AddWithValue("@error", (object?)message.ErrorLog ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                if (opened) _connection.Close();
            }
        }

        public async Task<IEnumerable<OutboxMessage>> GetPendingAsync(int limit = 100)
        {
            var list = new List<OutboxMessage>();
            const string sql = @"SELECT id, aggregate_id, routing_key, payload, status, created_at, published_at, attempt_count, error_log 
                                 FROM outbox WHERE status = 'PENDING' ORDER BY created_at ASC LIMIT @lim;";

            var opened = _transaction == null && _connection.State != ConnectionState.Open;
            if (opened) await _connection.OpenAsync();
            try
            {
                using var cmd = new MySqlCommand(sql, _connection, _transaction);
                cmd.Parameters.AddWithValue("@lim", limit);
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    // Leer id y aggregate_id de forma flexible: pueden venir como Guid o como string
                    string ReadStringFlexible(string col)
                    {
                        var ord = rdr.GetOrdinal(col);
                        if (rdr.IsDBNull(ord)) return string.Empty;
                        var val = rdr.GetValue(ord);
                        return val switch
                        {
                            Guid g => g.ToString(),
                            string s => s,
                            _ => val?.ToString() ?? string.Empty
                        };
                    }

                    list.Add(new OutboxMessage
                    {
                        Id = ReadStringFlexible("id"),
                        AggregateId = ReadStringFlexible("aggregate_id"),
                        RoutingKey = rdr.IsDBNull(rdr.GetOrdinal("routing_key")) ? string.Empty : rdr.GetString("routing_key"),
                        Payload = rdr.IsDBNull(rdr.GetOrdinal("payload")) ? string.Empty : rdr.GetString("payload"),
                        Status = rdr.IsDBNull(rdr.GetOrdinal("status")) ? string.Empty : rdr.GetString("status"),
                        CreatedAt = rdr.GetDateTime("created_at"),
                        PublishedAt = rdr.IsDBNull(rdr.GetOrdinal("published_at")) ? null : rdr.GetDateTime("published_at"),
                        AttemptCount = rdr.GetInt32("attempt_count"),
                        ErrorLog = rdr.IsDBNull(rdr.GetOrdinal("error_log")) ? null : rdr.GetString("error_log")
                    });
                }
            }
            finally
            {
                if (opened) _connection.Close();
            }

            return list;
        }

        public async Task MarkSentAsync(string id)
        {
            const string sql = @"UPDATE outbox SET status = 'PUBLISHED', published_at = @published_at WHERE id = @id;";

            var opened = _transaction == null && _connection.State != ConnectionState.Open;
            if (opened) await _connection.OpenAsync();
            try
            {
                using var cmd = new MySqlCommand(sql, _connection, _transaction);
                cmd.Parameters.AddWithValue("@published_at", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                if (opened) _connection.Close();
            }
        }

        public async Task IncrementAttemptAsync(string id)
        {
            const string sql = @"UPDATE outbox SET attempt_count = attempt_count + 1 WHERE id = @id;";

            var opened = _transaction == null && _connection.State != ConnectionState.Open;
            if (opened) await _connection.OpenAsync();
            try
            {
                using var cmd = new MySqlCommand(sql, _connection, _transaction);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                if (opened) _connection.Close();
            }
        }
    }
}