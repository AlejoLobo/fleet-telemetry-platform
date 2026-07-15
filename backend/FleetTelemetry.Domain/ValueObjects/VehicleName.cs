using System.Text.RegularExpressions;
using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

public sealed class VehicleName : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 100;

    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    private VehicleName(string value) => Value = value;

    public static bool TryCreate(string? raw, out VehicleName? vehicleName, out string? error)
    {
        vehicleName = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "VehicleName is required.";
            return false;
        }

        var normalized = MultiSpace.Replace(raw.Trim(), " ");
        if (normalized.Length < MinLength)
        {
            error = $"VehicleName must be at least {MinLength} characters.";
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            error = $"VehicleName must be at most {MaxLength} characters.";
            return false;
        }

        vehicleName = new VehicleName(normalized);
        return true;
    }

    public static VehicleName Create(string raw) =>
        TryCreate(raw, out var vehicleName, out var error)
            ? vehicleName!
            : throw new ArgumentException(error, nameof(raw));

    public static string FormatAutomatic(long sequenceNumber)
    {
        if (sequenceNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber));

        return sequenceNumber <= 999
            ? $"VH-{sequenceNumber:D3}"
            : $"VH-{sequenceNumber}";
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
