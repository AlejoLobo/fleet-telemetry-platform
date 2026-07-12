using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Services;

// Validación estricta de payloads de cursor tras decodificación.
public static class CursorValidators
{
    private const int MaxVehicleIdLength = 64;

    public static void ValidateFleetCursor(FleetCursorPayload payload, bool liveOnly, bool excludeSimulated)
    {
        if (payload.Version != FleetCursorPayload.CurrentVersion)
            throw new InvalidCursorException("Versión de cursor no soportada.");

        if (string.IsNullOrWhiteSpace(payload.LastVehicleId))
            throw new InvalidCursorException("Cursor inválido.");

        if (payload.LastVehicleId.Length > MaxVehicleIdLength)
            throw new InvalidCursorException("Cursor inválido.");

        if (payload.LiveOnly != liveOnly || payload.ExcludeSimulated != excludeSimulated)
            throw new InvalidCursorException("El cursor no coincide con los filtros solicitados.");
    }

    public static void ValidateHistoryCursor(
        TelemetryHistoryCursorPayload payload,
        string vehicleId,
        DateTimeOffset from,
        DateTimeOffset to,
        int historyMaxRangeDays)
    {
        if (payload.Version != TelemetryHistoryCursorPayload.CurrentVersion)
            throw new InvalidCursorException("Versión de cursor no soportada.");

        if (string.IsNullOrWhiteSpace(payload.VehicleId))
            throw new InvalidCursorException("Cursor inválido.");

        if (!string.Equals(payload.VehicleId, vehicleId, StringComparison.Ordinal))
            throw new InvalidCursorException("El cursor no pertenece al vehículo solicitado.");

        if (payload.LastTimestamp is null)
            throw new InvalidCursorException("Cursor inválido.");

        if (payload.LastEventId is null || payload.LastEventId == Guid.Empty)
            throw new InvalidCursorException("Cursor inválido.");

        if (payload.From >= payload.To)
            throw new InvalidCursorException("Cursor inválido.");

        var maxRange = TimeSpan.FromDays(historyMaxRangeDays);
        if (payload.To - payload.From > maxRange)
            throw new InvalidCursorException("Cursor inválido.");

        if (payload.From != from || payload.To != to)
            throw new InvalidCursorException("El cursor no coincide con el rango solicitado.");

        if (payload.LastTimestamp < from || payload.LastTimestamp > to)
            throw new InvalidCursorException("Cursor inválido.");
    }
}
