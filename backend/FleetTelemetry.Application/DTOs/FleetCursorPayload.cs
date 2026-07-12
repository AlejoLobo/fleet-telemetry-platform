namespace FleetTelemetry.Application.DTOs;

// Contenido validado del cursor de flota.
public sealed record FleetCursorPayload(
    int Version,
    string? LastVehicleId,
    bool LiveOnly,
    bool ExcludeSimulated)
{
    public const int CurrentVersion = 1;
}
