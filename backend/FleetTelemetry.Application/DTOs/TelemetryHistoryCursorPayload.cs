namespace FleetTelemetry.Application.DTOs;

// Contenido validado del cursor de historial por dispositivo.
public sealed record TelemetryHistoryCursorPayload(
    int Version,
    Guid DeviceId,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? LastTimestamp,
    Guid? LastEventId)
{
    public const int CurrentVersion = 1;
}
