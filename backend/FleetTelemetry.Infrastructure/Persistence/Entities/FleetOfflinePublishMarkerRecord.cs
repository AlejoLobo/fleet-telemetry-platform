using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("fleet_offline_publish_markers")]
public class FleetOfflinePublishMarkerRecord
{
    [Key]
    [Column("device_id")]
    public Guid DeviceId { get; set; }

    public Guid LastEventId { get; set; }

    public DateTimeOffset StatusEvaluatedAt { get; set; }
}
