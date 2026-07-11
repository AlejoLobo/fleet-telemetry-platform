// Payload de un evento individual desde dispositivos o simuladores.
public record TelemetryEventRequest(
    Guid EventId,
    string VehicleId,
    string? DriverId,
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double SpeedKmh,
    double? FuelLevelPercent = null,
    double? BatteryPercent = null,
    string? LocationSource = null);
