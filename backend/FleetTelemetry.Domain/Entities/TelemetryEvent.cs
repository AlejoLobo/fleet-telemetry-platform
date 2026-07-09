// Entidad de evento de telemetría vehicular.
namespace FleetTelemetry.Domain.Entities;

// Datos de posición y estado reportados por un vehículo.
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
