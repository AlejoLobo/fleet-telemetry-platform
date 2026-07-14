using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

public sealed class AlertSeverity : ValueObject
{
    public static readonly AlertSeverity Info = new("info");
    public static readonly AlertSeverity Warning = new("warning");
    public static readonly AlertSeverity Critical = new("critical");

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "warning", "critical"
    };

    public string Value { get; }

    private AlertSeverity(string value) => Value = value;

    public static bool TryCreate(string? raw, out AlertSeverity? severity, out string? error)
    {
        severity = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Alert severity is required.";
            return false;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (!Allowed.Contains(normalized))
        {
            error = "Alert severity must be info, warning or critical.";
            return false;
        }

        severity = normalized switch
        {
            "warning" => Warning,
            "critical" => Critical,
            _ => Info
        };
        return true;
    }

    public static AlertSeverity Create(string raw) =>
        TryCreate(raw, out var severity, out var error)
            ? severity!
            : throw new ArgumentException(error, nameof(raw));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
