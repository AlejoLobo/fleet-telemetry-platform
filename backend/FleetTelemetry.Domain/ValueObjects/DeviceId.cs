using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

public sealed class DeviceId : ValueObject
{
    public Guid Value { get; }

    private DeviceId(Guid value) => Value = value;

    public static bool TryCreate(Guid raw, out DeviceId? deviceId, out string? error)
    {
        deviceId = null;
        error = null;

        if (raw == Guid.Empty)
        {
            error = "DeviceId is required.";
            return false;
        }

        deviceId = new DeviceId(raw);
        return true;
    }

    public static DeviceId Create(Guid raw) =>
        TryCreate(raw, out var deviceId, out var error)
            ? deviceId!
            : throw new ArgumentException(error, nameof(raw));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}
