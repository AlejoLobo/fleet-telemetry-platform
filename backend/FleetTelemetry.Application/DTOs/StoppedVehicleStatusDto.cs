namespace FleetTelemetry.Application.DTOs;

public record StoppedVehicleStatusDto(
    string VehicleId,
    DateTimeOffset LastSeenAt,
    DateTimeOffset StoppedSince,
    TimeSpan StoppedDuration,
    double Latitude,
    double Longitude,
    string? CriticalZoneName);
