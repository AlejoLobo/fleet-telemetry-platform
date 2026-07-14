using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

public sealed class VehicleId : ValueObject
{
    public string Value { get; }

    private VehicleId(string value) => Value = value;

    public static bool TryCreate(string? raw, out VehicleId? vehicleId, out string? error)
    {
        vehicleId = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "VehicleId is required.";
            return false;
        }

        var normalized = raw.Trim();
        if (normalized.Length > 64)
        {
            error = "VehicleId must be at most 64 characters.";
            return false;
        }

        vehicleId = new VehicleId(normalized);
        return true;
    }

    public static VehicleId Create(string raw) =>
        TryCreate(raw, out var vehicleId, out var error)
            ? vehicleId!
            : throw new ArgumentException(error, nameof(raw));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
