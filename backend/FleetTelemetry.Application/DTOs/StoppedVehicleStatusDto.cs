namespace FleetTelemetry.Application.DTOs;

public record StoppedVehicleStatusDto(
    Guid DeviceId,
    DateTimeOffset LastSeenAt,
    DateTimeOffset StoppedSince,
    TimeSpan StoppedDuration,
    double Latitude,
    double Longitude,
    string? CriticalZoneName);
