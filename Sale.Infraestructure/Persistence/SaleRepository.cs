using MySql.Data.MySqlClient;
using Sale.Domain.Entities;
using Sale.Domain.Ports;
using Sale.Infraestructure.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Sale.Infraestructure.Persistence
{
    public class SaleRepository : ISaleRepository
    {
        private readonly DatabaseConnection _db;

        private readonly MySqlConnection _connection;
        private readonly MySqlTransaction? _transaction;
        public SaleRepository(MySqlConnection connection, MySqlTransaction? transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public async Task<SaleEntity> Create(SaleEntity entity)
        {
            if (string.IsNullOrWhiteSpace(entity.id))
                entity.id = Guid.NewGuid().ToString();

             const string query = @"
INSERT INTO sales
(id, `date`, total_amount, client_id, status, rejection_reason, is_deleted, created_at, created_by)
VALUES
(@id, @date, @total_amount, @client_id, @status, @rejection_reason, @is_deleted, @created_at, @created_by);";

            using var comand = new MySqlCommand(query, _connection, _transaction);
            comand.Parameters.AddWithValue("@id", entity.id);
            comand.Parameters.AddWithValue("@date", entity.date);
            comand.Parameters.AddWithValue("@total_amount", entity.totalAmount);
            comand.Parameters.AddWithValue("@client_id", entity.clientId);
            comand.Parameters.AddWithValue("@status", entity.status);
            comand.Parameters.AddWithValue("@rejection_reason", (object?)entity.rejection_reason ?? DBNull.Value);
            comand.Parameters.AddWithValue("@created_at", entity.created_at);
            comand.Parameters.AddWithValue("@created_by", (object?)entity.created_by ?? DBNull.Value);
            comand.Parameters.AddWithValue("@is_deleted", entity.is_deleted);

            await comand.ExecuteNonQueryAsync();
            return entity;
        }

        public async Task<SaleEntity?> GetById(SaleEntity entity)
        {
            const string query = "SELECT * FROM sales WHERE id = @id AND is_deleted = FALSE;";
            using var comand = new MySqlCommand(query, _connection, _transaction);
            comand.Parameters.AddWithValue("@id", entity.id);

            using var reader = await comand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var s = new SaleEntity
                {
                    id = reader.GetString("id"),
                    date = reader.GetDateTime("date"),
                    totalAmount = reader.GetDecimal("total_amount"),
                    clientId = reader.GetString("client_id"),
                    status = reader.GetString("status"),
                    rejection_reason = reader.IsDBNull(reader.GetOrdinal("rejection_reason")) ? null : reader.GetString("rejection_reason"),

                    is_deleted = reader.GetBoolean("is_deleted"),
                    created_by = reader.IsDBNull(reader.GetOrdinal("created_by")) ? null : reader.GetString("created_by"),
                    updated_by = reader.IsDBNull(reader.GetOrdinal("updated_by")) ? null : reader.GetString("updated_by"),
                    created_at = reader.GetDateTime("created_at"),
                    updated_at = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetDateTime("updated_at")
                };

                return s;
            }

            return null;
        }

        public async Task<IEnumerable<SaleEntity>> GetAll()
        {
            var lista = new List<SaleEntity>();
            const string query = "SELECT * FROM sales WHERE is_deleted = FALSE ORDER BY `date` ASC;";

            using var comand = new MySqlCommand(query, _connection, _transaction);
            using var reader = await comand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new SaleEntity
                {
                    id = reader.GetString("id"),
                    date = reader.GetDateTime("date"),
                    totalAmount = reader.GetDecimal("total_amount"),
                    clientId = reader.GetString("client_id"),
                    status = reader.GetString("status"),

                    is_deleted = reader.GetBoolean("is_deleted"),
                    created_by = reader.IsDBNull(reader.GetOrdinal("created_by")) ? null : reader.GetString("created_by"),
                    updated_by = reader.IsDBNull(reader.GetOrdinal("updated_by")) ? null : reader.GetString("updated_by"),
                    created_at = reader.GetDateTime("created_at"),
                    updated_at = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetDateTime("updated_at")
                });
            }

            return lista;
        }

        public async Task Update(SaleEntity entity)
        {
            const string query = @"
UPDATE sales
SET `date` = @date,
    total_amount = @total_amount,
    client_id = @client_id,
    status = @status,
    rejection_reason = @rejection_reason,
    updated_at = @updated_at,
    updated_by = @updated_by
WHERE id = @id;";

            using var comand = new MySqlCommand(query, _connection, _transaction);
            comand.Parameters.AddWithValue("@date", entity.date);
            comand.Parameters.AddWithValue("@total_amount", entity.totalAmount);
            comand.Parameters.AddWithValue("@client_id", entity.clientId);
            comand.Parameters.AddWithValue("@status", entity.status);
            comand.Parameters.AddWithValue("@rejection_reason", (object?)entity.rejection_reason ?? DBNull.Value);
            comand.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
            comand.Parameters.AddWithValue("@updated_by", (object?)entity.updated_by ?? DBNull.Value);
            comand.Parameters.AddWithValue("@id", entity.id);

            await comand.ExecuteNonQueryAsync();
        }

        public async Task Delete(SaleEntity entity)
        {
            const string query = @"
UPDATE sales
SET is_deleted = TRUE,
    updated_at = @updated_at,
    updated_by = @updated_by
WHERE id = @id;";

            using var cmd = new MySqlCommand(query, _connection, _transaction);
            cmd.Parameters.AddWithValue("@id", entity.id);
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@updated_by", (object?)entity.updated_by ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

    }
}
