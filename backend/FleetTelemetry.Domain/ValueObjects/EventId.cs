using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

// Identificador único de evento de telemetría.
public sealed class EventId : ValueObject
{
    public Guid Value { get; }

    private EventId(Guid value) => Value = value;

    public static bool TryCreate(Guid raw, out EventId? eventId, out string? error)
    {
        eventId = null;
        error = null;

        if (raw == Guid.Empty)
        {
            error = "EventId is required.";
            return false;
        }

        eventId = new EventId(raw);
        return true;
    }

    public static EventId Create(Guid raw) =>
        TryCreate(raw, out var eventId, out var error)
            ? eventId!
            : throw new ArgumentException(error, nameof(raw));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}
