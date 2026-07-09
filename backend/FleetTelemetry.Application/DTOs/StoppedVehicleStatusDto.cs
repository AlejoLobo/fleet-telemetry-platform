// DTO de vehículo detenido por tiempo prolongado.
namespace FleetTelemetry.Application.DTOs;

// Estado de detención con ubicación y zona crítica opcional.
public record StoppedVehicleStatusDto(
    string VehicleId,
    DateTimeOffset LastSeenAt,
    DateTimeOffset StoppedSince,
    TimeSpan StoppedDuration,
    double Latitude,
    double Longitude,
    string? CriticalZoneName);
