namespace FleetTelemetry.Application.Realtime;

// Representación explícita del cursor SSE: ausente, válido o inválido.
public abstract record SseLastEventId
{
    private SseLastEventId()
    {
    }

    public sealed record Missing : SseLastEventId;

    public sealed record ValidCursor(long Value) : SseLastEventId;

    public sealed record InvalidCursor : SseLastEventId;

    public static SseLastEventId Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Missing();

        return long.TryParse(raw, out var parsed) && parsed >= 0
            ? new ValidCursor(parsed)
            : new InvalidCursor();
    }
}
