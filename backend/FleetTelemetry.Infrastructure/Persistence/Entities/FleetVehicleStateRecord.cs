using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FleetTelemetry.Infrastructure.Persistence.Entities;

[Table("fleet_vehicle_state")]
public class FleetVehicleStateRecord
{
    [Key]
    [Column("device_id")]
    public Guid DeviceId { get; set; }

    public Guid LastEventId { get; set; }

    [MaxLength(64)]
    public string? DriverId { get; set; }

    public DateTimeOffset LastTimestamp { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double SpeedKmh { get; set; }

    public double? FuelLevelPercent { get; set; }

    public double? BatteryPercent { get; set; }

    [MaxLength(16)]
    public string LocationSource { get; set; } = "gps";

    public DateTimeOffset UpdatedAt { get; set; }
}
