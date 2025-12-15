using MySql.Data.MySqlClient;
using Sale.Domain.Ports;
using Sale.Infraestructure.Data;
using System;
using System.Threading.Tasks;

namespace Sale.Infraestructure.Persistence
{
    public class IdempotencyRepository : IIdempotencyStore
    {
        private readonly DatabaseConnection _db;
        public IdempotencyRepository()
        {
            _db = DatabaseConnection.Instance;
        }
        public async Task<bool> HasProcessedAsync(string messageId)
        {
            const string sql = "SELECT 1 FROM inbox_history WHERE message_id = @id LIMIT 1;";
            using var con = _db.GetConnection();
            await con.OpenAsync();
            using var cmd = new MySqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@id", messageId);
            using var rdr = await cmd.ExecuteReaderAsync();
            return await rdr.ReadAsync();
        }

        public async Task MarkProcessedAsync(string messageId, string routingKey)
        {
            const string sql = @"INSERT IGNORE INTO inbox_history (message_id, routing_key, processed_at, status) 
                                 VALUES (@id, @rk, @at, 'PROCESSED');";
            using var con = _db.GetConnection();
            await con.OpenAsync();
            using var cmd = new MySqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@id", messageId);
            cmd.Parameters.AddWithValue("@rk", routingKey);
            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}