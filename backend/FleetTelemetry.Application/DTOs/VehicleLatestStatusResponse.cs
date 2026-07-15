namespace FleetTelemetry.Application.DTOs;

public record VehicleLatestStatusResponse(
    string VehicleId,
    string Name,
    string Status,
    DateTimeOffset? LastSeenAt,
    double? LastSpeedKmh,
    double? LastLatitude,
    double? LastLongitude,
    double? LastHeadingDegrees,
    string? LastLocationSource = null,
    Guid? LastEventId = null,
    DateTimeOffset? StatusEvaluatedAt = null,
    string? DriverId = null,
    /// <summary>ID estable del dispositivo (UUID móvil). Coincide con VehicleId en la ingesta mobile.</summary>
    string? DeviceId = null);
