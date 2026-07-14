using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

public sealed class AlertType : ValueObject
{
    public string Value { get; }

    private AlertType(string value) => Value = value;

    public static bool TryCreate(string? raw, out AlertType? alertType, out string? error)
    {
        alertType = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "AlertType is required.";
            return false;
        }

        var normalized = raw.Trim();
        if (normalized.Length > 64)
        {
            error = "AlertType must be at most 64 characters.";
            return false;
        }

        alertType = new AlertType(normalized);
        return true;
    }

    public static AlertType Create(string raw) =>
        TryCreate(raw, out var alertType, out var error)
            ? alertType!
            : throw new ArgumentException(error, nameof(raw));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
