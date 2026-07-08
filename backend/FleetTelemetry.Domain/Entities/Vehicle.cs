namespace FleetTelemetry.Domain.Entities;

public class Vehicle
{
    public string VehicleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "offline";
    public DateTimeOffset? LastSeenAt { get; set; }
}
