using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// Entidad ORM de evento de telemetría.
namespace FleetTelemetry.Infrastructure.Persistence.Entities;

// Mapeo a hypertable telemetry_events.
[Table("telemetry_events")]
public class TelemetryEventRecord
{
    public Guid EventId { get; set; }

    [MaxLength(64)]
    public string VehicleId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? DriverId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double SpeedKmh { get; set; }

    public double? FuelLevelPercent { get; set; }

    public double? BatteryPercent { get; set; }

    public DateTimeOffset CapturedAt { get; set; }
}
