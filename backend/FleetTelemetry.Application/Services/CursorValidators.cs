using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Services;

// Validaci?n estricta de payloads de cursor tras decodificaci?n.
public static class CursorValidators
{
    public static void ValidateFleetCursor(FleetCursorPayload payload, bool liveOnly, bool excludeSimulated)
    {
        if (payload.Version != FleetCursorPayload.CurrentVersion)
            throw new InvalidCursorException("Versi?n de cursor no soportada.");

        if (payload.LastDeviceId == Guid.Empty)
            throw new InvalidCursorException("Cursor inv?lido.");

        if (payload.LiveOnly != liveOnly || payload.ExcludeSimulated != excludeSimulated)
            throw new InvalidCursorException("El cursor no coincide con los filtros solicitados.");
    }

    public static void ValidateHistoryCursor(
        TelemetryHistoryCursorPayload payload,
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int historyMaxRangeDays)
    {
        if (payload.Version != TelemetryHistoryCursorPayload.CurrentVersion)
            throw new InvalidCursorException("Versi?n de cursor no soportada.");

        if (payload.DeviceId == Guid.Empty)
            throw new InvalidCursorException("Cursor inv?lido.");

        if (payload.DeviceId != deviceId)
            throw new InvalidCursorException("El cursor no pertenece al dispositivo solicitado.");

        if (payload.LastTimestamp is null)
            throw new InvalidCursorException("Cursor inv?lido.");

        if (payload.LastEventId is null || payload.LastEventId == Guid.Empty)
            throw new InvalidCursorException("Cursor inv?lido.");

        if (payload.From >= payload.To)
            throw new InvalidCursorException("Cursor inv?lido.");

        var maxRange = TimeSpan.FromDays(historyMaxRangeDays);
        if (payload.To - payload.From > maxRange)
            throw new InvalidCursorException("Cursor inv?lido.");

        if (payload.From != from || payload.To != to)
            throw new InvalidCursorException("El cursor no coincide con el rango solicitado.");

        if (payload.LastTimestamp < from || payload.LastTimestamp > to)
            throw new InvalidCursorException("Cursor inv?lido.");
    }
}
