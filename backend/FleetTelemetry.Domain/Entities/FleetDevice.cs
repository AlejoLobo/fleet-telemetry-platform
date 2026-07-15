using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

/// <summary>
/// Dispositivo físico estable. DeviceId es inmutable; VehicleName es editable.
/// </summary>
public sealed class FleetDevice
{
    public Guid DeviceId => _deviceId.Value;
    public string VehicleName => _vehicleName.Value;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly ValueObjects.DeviceId _deviceId;
    private ValueObjects.VehicleName _vehicleName;

    private FleetDevice(
        ValueObjects.DeviceId deviceId,
        ValueObjects.VehicleName vehicleName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        _deviceId = deviceId;
        _vehicleName = vehicleName;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static FleetDevice Create(
        Guid deviceId,
        string vehicleName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(
            ValueObjects.DeviceId.Create(deviceId),
            ValueObjects.VehicleName.Create(vehicleName),
            createdAt,
            updatedAt);

    public static FleetDevice FromPersistence(
        Guid deviceId,
        string vehicleName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        Create(deviceId, vehicleName, createdAt, updatedAt);

    public void Rename(string vehicleName, DateTimeOffset updatedAt)
    {
        _vehicleName = ValueObjects.VehicleName.Create(vehicleName);
        UpdatedAt = updatedAt;
    }
}
