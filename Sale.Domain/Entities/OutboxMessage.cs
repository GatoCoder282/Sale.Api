using System;

namespace Sale.Domain.Entities
{
    public class OutboxMessage
    {
        // id CHAR(36)
        public string Id { get; set; } = string.Empty;
        // aggregate_id CHAR(36) (p. ej. sale id)
        public string AggregateId { get; set; } = string.Empty;
        public string RoutingKey { get; set; } = string.Empty;
        // payload JSON text
        public string Payload { get; set; } = string.Empty;
        public string Status { get; set; } = "PENDING"; // PENDING | PUBLISHED | FAILED
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedAt { get; set; }
        public int AttemptCount { get; set; } = 0;
        public string? ErrorLog { get; set; }
    }
}