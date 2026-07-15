namespace FleetTelemetry.Application.DTOs;

public record TelemetryEventResponse(
    Guid EventId,
    Guid DeviceId,
    string? DriverId,
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double SpeedKmh,
    double? FuelLevelPercent,
    double? BatteryPercent,
    string LocationSource);
