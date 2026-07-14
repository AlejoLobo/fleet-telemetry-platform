using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("fleet_alert_states")]
public class FleetAlertConditionStateRecord
{
    [MaxLength(64)]
    public string VehicleId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string AlertType { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset LastConditionAt { get; set; }

    public DateTimeOffset? LastAlertAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
