namespace FleetTelemetry.Application.DTOs;

public record TelemetryEventRequest(
    Guid EventId,
    Guid DeviceId,
    string? DriverId,
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double SpeedKmh,
    double? FuelLevelPercent = null,
    double? BatteryPercent = null,
    string? LocationSource = null);
