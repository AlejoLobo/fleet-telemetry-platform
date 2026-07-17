using System.Collections.Concurrent;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Application.Tests;

/// <summary>
/// Contrato de registro idempotente sin base de datos (Paso 2).
/// </summary>
public class DeviceRegistryContractTests
{
    [Fact]
    public async Task Register_is_idempotent_for_same_device_id()
    {
        var registry = new InMemoryDeviceRegistry();
        var deviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var first = await registry.RegisterDeviceAsync(deviceId);
        var second = await registry.RegisterDeviceAsync(deviceId);

        Assert.Equal(first.DeviceId, second.DeviceId);
        Assert.Equal(first.VehicleName, second.VehicleName);
        Assert.Equal("VH-001", first.VehicleName);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task Register_assigns_distinct_automatic_names()
    {
        var registry = new InMemoryDeviceRegistry();
        var a = await registry.RegisterDeviceAsync(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var b = await registry.RegisterDeviceAsync(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        Assert.Equal("VH-001", a.VehicleName);
        Assert.Equal("VH-002", b.VehicleName);
        Assert.NotEqual(a.DeviceId, b.DeviceId);
    }

    [Fact]
    public async Task Concurrent_registers_never_share_vehicle_name()
    {
        var registry = new InMemoryDeviceRegistry();
        var deviceIds = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();

        var devices = await Task.WhenAll(deviceIds.Select(id => registry.RegisterDeviceAsync(id)));

        Assert.Equal(deviceIds.Length, devices.Select(d => d.DeviceId).Distinct().Count());
        Assert.Equal(deviceIds.Length, devices.Select(d => d.VehicleName).Distinct().Count());
        Assert.Equal(deviceIds.Length, registry.Count);
    }

    [Fact]
    public async Task Rename_keeps_device_id_and_rejects_duplicate_name()
    {
        var registry = new InMemoryDeviceRegistry();
        var a = await registry.RegisterDeviceAsync(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        var b = await registry.RegisterDeviceAsync(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));

        var renamed = await registry.RenameDeviceAsync(a.DeviceId, "Camión Pereira");
        Assert.Equal(a.DeviceId, renamed.DeviceId);
        Assert.Equal("Camión Pereira", renamed.VehicleName);

        await Assert.ThrowsAsync<VehicleNameConflictException>(() =>
            registry.RenameDeviceAsync(b.DeviceId, "Camión Pereira"));

        await Assert.ThrowsAsync<DeviceNotFoundException>(() =>
            registry.RenameDeviceAsync(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), "Otro"));
    }

    private sealed class InMemoryDeviceRegistry : IDeviceRegistry
    {
        private readonly ConcurrentDictionary<Guid, FleetDevice> _byId = new();
        private readonly ConcurrentDictionary<string, Guid> _nameOwners = new(StringComparer.Ordinal);
        private long _sequence;

        public int Count => _byId.Count;

        public Task<FleetDevice?> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            _byId.TryGetValue(deviceId, out var device);
            return Task.FromResult(device);
        }

        public Task<FleetDevice> RegisterDeviceAsync(
            Guid deviceId,
            string? vehicleType = null,
            CancellationToken cancellationToken = default)
        {
            if (deviceId == Guid.Empty)
                throw new InvalidDeviceIdException("DeviceId is required.");

            var type = string.IsNullOrWhiteSpace(vehicleType)
                ? VehicleType.Default.Value
                : VehicleType.Create(vehicleType).Value;

            while (true)
            {
                if (_byId.TryGetValue(deviceId, out var existing))
                    return Task.FromResult(existing);

                var sequence = Interlocked.Increment(ref _sequence);
                var name = VehicleName.FormatAutomatic(sequence);
                var now = DateTimeOffset.UtcNow;
                var created = FleetDevice.Create(deviceId, name, now, now, type);

                if (!_nameOwners.TryAdd(name, deviceId))
                    continue;

                if (_byId.TryAdd(deviceId, created))
                    return Task.FromResult(created);

                _nameOwners.TryRemove(name, out _);
            }
        }

        public Task<FleetDevice> RenameDeviceAsync(
            Guid deviceId,
            string vehicleName,
            CancellationToken cancellationToken = default) =>
            UpdateDeviceProfileAsync(deviceId, vehicleName, vehicleType: null, cancellationToken);

        public Task<FleetDevice> UpdateDeviceProfileAsync(
            Guid deviceId,
            string? vehicleName,
            string? vehicleType,
            CancellationToken cancellationToken = default)
        {
            VehicleName? normalized = null;
            if (!string.IsNullOrWhiteSpace(vehicleName))
            {
                if (!VehicleName.TryCreate(vehicleName, out normalized, out var error))
                    throw new InvalidVehicleNameException(error ?? "invalid");
            }

            string? normalizedType = null;
            if (!string.IsNullOrWhiteSpace(vehicleType))
            {
                if (!VehicleType.TryCreate(vehicleType, out var parsed, out var typeError))
                    throw new InvalidVehicleTypeException(typeError ?? "invalid");
                normalizedType = parsed!.Value;
            }

            if (normalized is null && normalizedType is null)
                throw new InvalidVehicleNameException("At least one of vehicleName or vehicleType is required.");

            if (!_byId.TryGetValue(deviceId, out var current))
                throw new DeviceNotFoundException(deviceId);

            if (normalized is not null
                && _nameOwners.TryGetValue(normalized.Value, out var owner)
                && owner != deviceId)
                throw new VehicleNameConflictException(normalized.Value);

            var now = DateTimeOffset.UtcNow;
            var nextName = normalized?.Value ?? current.VehicleName;
            var nextType = normalizedType ?? current.VehicleType;
            var updated = FleetDevice.Create(current.DeviceId, nextName, current.CreatedAt, now, nextType);

            if (!string.Equals(current.VehicleName, nextName, StringComparison.Ordinal))
            {
                _nameOwners.TryRemove(current.VehicleName, out _);
                _nameOwners[nextName] = deviceId;
            }

            _byId[deviceId] = updated;
            return Task.FromResult(updated);
        }
    }
}
