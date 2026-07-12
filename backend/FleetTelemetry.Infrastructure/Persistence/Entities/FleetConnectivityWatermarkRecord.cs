using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("fleet_connectivity_watermark")]
public class FleetConnectivityWatermarkRecord
{
    [Key]
    public int Id { get; set; } = 1;

    public DateTimeOffset PreviousOnlineThreshold { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
