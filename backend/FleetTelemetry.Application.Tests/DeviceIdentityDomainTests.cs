using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Application.Tests;

public class DeviceIdentityDomainTests
{
    [Fact]
    public void DeviceId_rejects_empty_guid()
    {
        Assert.False(DeviceId.TryCreate(Guid.Empty, out var deviceId, out var error));
        Assert.Null(deviceId);
        Assert.Contains("required", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeviceId_accepts_valid_guid()
    {
        var value = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.True(DeviceId.TryCreate(value, out var deviceId, out var error));
        Assert.Equal(value, deviceId!.Value);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void VehicleName_rejects_invalid(string? raw)
    {
        Assert.False(VehicleName.TryCreate(raw, out var name, out var error));
        Assert.Null(name);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void VehicleName_normalizes_spaces_and_accepts_bounds()
    {
        Assert.True(VehicleName.TryCreate("  Camión   Pereira  ", out var name, out var error));
        Assert.Equal("Camión Pereira", name!.Value);
        Assert.Null(error);

        var longName = new string('x', VehicleName.MaxLength);
        Assert.True(VehicleName.TryCreate(longName, out _, out _));
        Assert.False(VehicleName.TryCreate(longName + "y", out _, out _));
    }

    [Theory]
    [InlineData(1, "VH-001")]
    [InlineData(2, "VH-002")]
    [InlineData(3, "VH-003")]
    [InlineData(999, "VH-999")]
    [InlineData(1000, "VH-1000")]
    public void VehicleName_format_automatic_matches_expected(long sequence, string expected)
    {
        Assert.Equal(expected, VehicleName.FormatAutomatic(sequence));
    }

    [Fact]
    public void FleetDevice_rename_updates_name_without_changing_device_id()
    {
        var deviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var createdAt = DateTimeOffset.Parse("2026-07-15T10:00:00Z");
        var device = FleetDevice.Create(deviceId, "VH-001", createdAt, createdAt);

        var renamedAt = DateTimeOffset.Parse("2026-07-15T11:00:00Z");
        device.Rename("Camión Pereira", renamedAt);

        Assert.Equal(deviceId, device.DeviceId);
        Assert.Equal("Camión Pereira", device.VehicleName);
        Assert.Equal(createdAt, device.CreatedAt);
        Assert.Equal(renamedAt, device.UpdatedAt);
    }
}
