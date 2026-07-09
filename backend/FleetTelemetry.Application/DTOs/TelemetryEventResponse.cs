namespace FleetTelemetry.Application.DTOs;

public record TelemetryEventResponse(
    Guid EventId,
    string VehicleId,
    string? DriverId,
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double SpeedKmh,
    double? FuelLevelPercent,
    double? BatteryPercent);
