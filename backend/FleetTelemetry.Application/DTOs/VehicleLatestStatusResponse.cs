namespace FleetTelemetry.Application.DTOs;

public record VehicleLatestStatusResponse(
    string VehicleId,
    string Name,
    string Status,
    DateTimeOffset? LastSeenAt,
    double? LastSpeedKmh,
    double? LastLatitude,
    double? LastLongitude,
    double? LastHeadingDegrees);
