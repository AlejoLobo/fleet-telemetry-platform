// DTO del último estado conocido de un vehículo.
namespace FleetTelemetry.Application.DTOs;

// Posición, velocidad y conectividad recientes.
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
    Guid? LastEventId = null);
