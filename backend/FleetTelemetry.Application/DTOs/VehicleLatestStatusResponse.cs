namespace FleetTelemetry.Application.DTOs;

public record VehicleLatestStatusResponse(
    Guid DeviceId,
    string VehicleName,
    string VehicleType,
    string Status,
    DateTimeOffset? LastSeenAt,
    double? LastSpeedKmh,
    double? LastLatitude,
    double? LastLongitude,
    double? LastHeadingDegrees,
    string? LastLocationSource = null,
    Guid? LastEventId = null,
    DateTimeOffset? StatusEvaluatedAt = null,
    string? DriverId = null);
