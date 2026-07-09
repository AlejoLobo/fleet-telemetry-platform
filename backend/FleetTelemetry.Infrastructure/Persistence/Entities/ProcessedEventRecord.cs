using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// Entidad ORM de evento ya procesado (idempotencia).
namespace FleetTelemetry.Infrastructure.Persistence.Entities;

// Mapeo a tabla processed_events.
[Table("processed_events")]
public class ProcessedEventRecord
{
    [Key]
    public Guid EventId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
