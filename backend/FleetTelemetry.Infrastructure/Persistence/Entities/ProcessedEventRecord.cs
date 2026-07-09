using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("processed_events")]
public class ProcessedEventRecord
{
    [Key]
    public Guid EventId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
