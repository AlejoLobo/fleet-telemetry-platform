using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// Entidad ORM de alerta de flota.
namespace FleetTelemetry.Infrastructure.Persistence.Entities;

// Mapeo a tabla fleet_alerts.
[Table("fleet_alerts")]
public class FleetAlertRecord
{
    [Key]
    public Guid AlertId { get; set; }

    [MaxLength(64)]
    public string VehicleId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string AlertType { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Severity { get; set; } = "info";

    [MaxLength(512)]
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsAcknowledged { get; set; }
}
