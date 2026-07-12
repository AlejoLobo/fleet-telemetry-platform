namespace FleetTelemetry.Application.DTOs;

// Contenido validado del cursor de historial por vehículo.
public sealed record TelemetryHistoryCursorPayload(
    int Version,
    string VehicleId,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? LastTimestamp,
    Guid? LastEventId)
{
    public const int CurrentVersion = 1;
}
