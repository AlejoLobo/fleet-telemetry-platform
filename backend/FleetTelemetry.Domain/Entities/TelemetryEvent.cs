namespace FleetTelemetry.Domain.Entities;

public class TelemetryEvent
{
    public Guid EventId { get; set; }
    public string VehicleId { get; set; } = string.Empty;
    public string? DriverId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKmh { get; set; }
    public double? FuelLevelPercent { get; set; }
    public double? BatteryPercent { get; set; }
}
