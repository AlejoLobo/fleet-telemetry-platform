using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("fleet_offline_publish_markers")]
public class FleetOfflinePublishMarkerRecord
{
    [Key]
    [MaxLength(64)]
    public string VehicleId { get; set; } = string.Empty;

    public Guid LastEventId { get; set; }

    public DateTimeOffset StatusEvaluatedAt { get; set; }
}
