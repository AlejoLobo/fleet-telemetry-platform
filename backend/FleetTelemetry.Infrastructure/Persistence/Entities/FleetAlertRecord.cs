using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("fleet_alerts")]
public class FleetAlertRecord
{
    [Key]
    public Guid AlertId { get; set; }

    [Column("device_id")]
    public Guid DeviceId { get; set; }

    [MaxLength(64)]
    public string AlertType { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Severity { get; set; } = "info";

    [MaxLength(512)]
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsAcknowledged { get; set; }
}
