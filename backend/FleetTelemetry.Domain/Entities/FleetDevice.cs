using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

/// <summary>
/// Dispositivo físico estable. DeviceId es inmutable; VehicleName y VehicleType son editables.
/// </summary>
public sealed class FleetDevice
{
    public Guid DeviceId => _deviceId.Value;
    public string VehicleName => _vehicleName.Value;
    public string VehicleType => _vehicleType.Value;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly ValueObjects.DeviceId _deviceId;
    private ValueObjects.VehicleName _vehicleName;
    private ValueObjects.VehicleType _vehicleType;

    private FleetDevice(
        ValueObjects.DeviceId deviceId,
        ValueObjects.VehicleName vehicleName,
        ValueObjects.VehicleType vehicleType,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        _deviceId = deviceId;
        _vehicleName = vehicleName;
        _vehicleType = vehicleType;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static FleetDevice Create(
        Guid deviceId,
        string vehicleName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? vehicleType = null) =>
        new(
            ValueObjects.DeviceId.Create(deviceId),
            ValueObjects.VehicleName.Create(vehicleName),
            string.IsNullOrWhiteSpace(vehicleType)
                ? ValueObjects.VehicleType.Default
                : ValueObjects.VehicleType.Create(vehicleType),
            createdAt,
            updatedAt);

    public static FleetDevice FromPersistence(
        Guid deviceId,
        string vehicleName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? vehicleType = null) =>
        new(
            ValueObjects.DeviceId.Create(deviceId),
            ValueObjects.VehicleName.Create(vehicleName),
            ValueObjects.VehicleType.ParseOrDefault(vehicleType),
            createdAt,
            updatedAt);

    public void Rename(string vehicleName, DateTimeOffset updatedAt)
    {
        _vehicleName = ValueObjects.VehicleName.Create(vehicleName);
        UpdatedAt = updatedAt;
    }

    public void ChangeVehicleType(string vehicleType, DateTimeOffset updatedAt)
    {
        _vehicleType = ValueObjects.VehicleType.Create(vehicleType);
        UpdatedAt = updatedAt;
    }
}
