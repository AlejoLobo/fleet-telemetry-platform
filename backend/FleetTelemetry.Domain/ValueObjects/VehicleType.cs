using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

/// <summary>
/// Catálogo cerrado de tipos de vehículo. Persistencia y API en minúsculas (p. ej. "motorcycle").
/// </summary>
public sealed class VehicleType : ValueObject
{
    public const string CarCode = "car";
    public const string MotorcycleCode = "motorcycle";
    public const string VanCode = "van";
    public const string TruckCode = "truck";
    public const string BusCode = "bus";
    public const string PickupCode = "pickup";

    public static readonly VehicleType Car = new(CarCode);
    public static readonly VehicleType Motorcycle = new(MotorcycleCode);
    public static readonly VehicleType Van = new(VanCode);
    public static readonly VehicleType Truck = new(TruckCode);
    public static readonly VehicleType Bus = new(BusCode);
    public static readonly VehicleType Pickup = new(PickupCode);

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        CarCode,
        MotorcycleCode,
        VanCode,
        TruckCode,
        BusCode,
        PickupCode
    };

    public string Value { get; }

    private VehicleType(string value) => Value = value;

    public static IReadOnlyCollection<string> AllowedCodes => Allowed;

    public static bool TryCreate(string? raw, out VehicleType? vehicleType, out string? error)
    {
        vehicleType = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "VehicleType is required.";
            return false;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (!Allowed.Contains(normalized))
        {
            error =
                $"VehicleType '{raw}' is invalid. Allowed values: {string.Join(", ", Allowed.OrderBy(x => x, StringComparer.Ordinal))}.";
            return false;
        }

        vehicleType = FromNormalized(normalized);
        return true;
    }

    public static VehicleType Create(string raw) =>
        TryCreate(raw, out var vehicleType, out var error)
            ? vehicleType!
            : throw new ArgumentException(error, nameof(raw));

    /// <summary>Valor por defecto para registros legacy sin tipo.</summary>
    public static VehicleType Default => Car;

    public static VehicleType ParseOrDefault(string? raw) =>
        TryCreate(raw, out var vehicleType, out _) ? vehicleType! : Default;

    private static VehicleType FromNormalized(string normalized) =>
        normalized switch
        {
            CarCode => Car,
            MotorcycleCode => Motorcycle,
            VanCode => Van,
            TruckCode => Truck,
            BusCode => Bus,
            PickupCode => Pickup,
            _ => throw new ArgumentException($"VehicleType '{normalized}' is invalid.", nameof(normalized))
        };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
