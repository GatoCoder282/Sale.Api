using Sale.Domain.Entities;
using Sale.Domain.Ports;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sale.Application.Services
{
    public class SaleService : ISaleService
    {
        private readonly IUnitOfWork _uow; // Inyectamos SOLO el UnitOfWork

        public SaleService(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task<SaleEntity> CreateSaleAsync(SaleEntity sale, List<SaleItemPayload> items)
        {
            // 1. Lógica de Dominio
            if (string.IsNullOrEmpty(sale.id)) sale.id = Guid.NewGuid().ToString();
            sale.status = "PENDING_DETAILS";
            sale.created_at = DateTime.UtcNow;
            sale.totalAmount = 0;
            sale.is_deleted = false;

            // 2. Preparar Outbox
            var eventPayload = new
            {
                MessageId = Guid.NewGuid().ToString(),
                sale_id = sale.id,
                client_id = sale.clientId,
                created_by = sale.created_by,
                items = items
            };

            var outboxMsg = new OutboxMessage
            {
                Id = Guid.NewGuid().ToString(),
                AggregateId = sale.id,
                RoutingKey = "sale.header.created",
                Payload = JsonSerializer.Serialize(eventPayload),
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            // 3. Persistencia Transaccional (Limpia)
            try
            {
                await _uow.BeginTransactionAsync();

                // Usamos los repositorios QUE PERTENECEN al UnitOfWork
                await _uow.SaleRepository.Create(sale);
                await _uow.OutboxRepository.AddAsync(outboxMsg);

                await _uow.CommitAsync();
            }
            catch
            {
                // El UoW hace rollback automático en CommitAsync si falla, 
                // pero si falla antes (en los repos), llamamos explícito o dejamos que Dispose limpie.
                await _uow.RollbackAsync();
                throw;
            }

            return sale;
        }

        public async Task<IEnumerable<SaleEntity>> GetAllSalesAsync()
        {
            await _uow.EnsureConnectionOpenAsync();
            return await _uow.SaleRepository.GetAll();
        }

        public async Task<SaleEntity?> GetSaleByIdAsync(string id)
        {
            await _uow.EnsureConnectionOpenAsync();
            return await _uow.SaleRepository.GetById(new SaleEntity { id = id });
        }

        public async Task UpdateSaleAsync(SaleEntity sale)
        {
            try
            {
                await _uow.BeginTransactionAsync();

                // 1. Actualización en Base de Datos
                sale.updated_at = DateTime.UtcNow;
                // Asumiendo que 'sale' ya trae los datos modificados o haces un merge aquí
                await _uow.SaleRepository.Update(sale);

                // 2. Notificar al mundo (Outbox)
                var eventPayload = new
                {
                    sale_id = sale.id,
                    updated_fields = sale, // O puedes enviar solo lo que cambió si quieres ser específico
                    updated_by = sale.updated_by,
                    updated_at = sale.updated_at
                };

                var outboxMsg = new OutboxMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    AggregateId = sale.id,
                    RoutingKey = "sale.updated", // Nueva Routing Key
                    Payload = JsonSerializer.Serialize(eventPayload),
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };

                await _uow.OutboxRepository.AddAsync(outboxMsg);

                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }
        }

        public async Task SoftDeleteSaleAsync(string id, string userId)
        {
            try
            {
                await _uow.BeginTransactionAsync();

                // 1. Borrado Lógico Local
                var entity = new SaleEntity { id = id, updated_by = userId };
                await _uow.SaleRepository.Delete(entity);

                // 2. Notificar al mundo (Compensación)
                // Esto le avisa a Medicinas que libere el stock y a Detalles que borre items
                var outboxMsg = new OutboxMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    AggregateId = id,
                    RoutingKey = "sale.cancelled", // Nuevo evento
                    Payload = JsonSerializer.Serialize(new { sale_id = id, reason = "Deleted by user" }),
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };
                await _uow.OutboxRepository.AddAsync(outboxMsg);

                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }
        }
    }
}