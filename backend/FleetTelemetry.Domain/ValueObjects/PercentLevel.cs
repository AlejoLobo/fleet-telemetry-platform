using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

// Porcentaje opcional acotado entre 0 y 100.
public sealed class PercentLevel : ValueObject
{
    public double Value { get; }

    private PercentLevel(double value) => Value = value;

    public static bool TryCreate(double? raw, out PercentLevel? level, out string? error)
    {
        level = null;
        error = null;

        if (raw is null)
            return true;

        if (raw is < 0 or > 100)
        {
            error = "Percent level must be between 0 and 100.";
            return false;
        }

        level = new PercentLevel(raw.Value);
        return true;
    }

    public static PercentLevel? CreateOptional(double? raw)
    {
        if (raw is null)
            return null;

        return TryCreate(raw, out var level, out var error)
            ? level
            : throw new ArgumentException(error, nameof(raw));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
