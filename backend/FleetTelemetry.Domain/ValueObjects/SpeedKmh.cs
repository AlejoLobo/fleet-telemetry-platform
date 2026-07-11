using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

// Velocidad en km/h no negativa.
public sealed class SpeedKmh : ValueObject
{
    public double Value { get; }

    private SpeedKmh(double value) => Value = value;

    public static bool TryCreate(double raw, out SpeedKmh? speed, out string? error)
    {
        speed = null;
        error = null;

        if (raw < 0)
        {
            error = "SpeedKmh must be >= 0.";
            return false;
        }

        speed = new SpeedKmh(raw);
        return true;
    }

    public static SpeedKmh Create(double raw) =>
        TryCreate(raw, out var speed, out var error)
            ? speed!
            : throw new ArgumentException(error, nameof(raw));

    public bool IsStopped(double thresholdKmh = 1.0) => Value <= thresholdKmh;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
