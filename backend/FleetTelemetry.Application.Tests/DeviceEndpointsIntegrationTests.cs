using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Application.Tests;

public class DeviceEndpointsIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Register_initial_assigns_vh001()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        using var response = await client.PostAsJsonAsync(
            "/api/devices/register",
            new RegisterDeviceRequest(deviceId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(deviceId, body!.DeviceId);
        Assert.Equal("VH-001", body.VehicleName);
    }

    [Fact]
    public async Task Register_repeated_returns_same_device()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using var first = await client.PostAsJsonAsync(
            "/api/devices/register",
            new RegisterDeviceRequest(deviceId));
        using var second = await client.PostAsJsonAsync(
            "/api/devices/register",
            new RegisterDeviceRequest(deviceId));

        var a = await first.Content.ReadFromJsonAsync<DeviceResponse>(JsonOptions);
        var b = await second.Content.ReadFromJsonAsync<DeviceResponse>(JsonOptions);
        Assert.Equal(a!.DeviceId, b!.DeviceId);
        Assert.Equal(a.VehicleName, b.VehicleName);
        Assert.Equal(1, factory.Registry.Count);
    }

    [Fact]
    public async Task Concurrent_registers_never_share_vehicle_name()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var deviceIds = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();

        var tasks = deviceIds.Select(async id =>
        {
            using var response = await client.PostAsJsonAsync(
                "/api/devices/register",
                new RegisterDeviceRequest(id));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<DeviceResponse>(JsonOptions))!;
        });

        var devices = await Task.WhenAll(tasks);
        Assert.Equal(deviceIds.Length, devices.Select(d => d.DeviceId).Distinct().Count());
        Assert.Equal(deviceIds.Length, devices.Select(d => d.VehicleName).Distinct().Count());
    }

    [Fact]
    public async Task Rename_updates_name_without_changing_device_id()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await client.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(deviceId));
        using var response = await client.PatchAsJsonAsync(
            $"/api/devices/{deviceId}/name",
            new RenameDeviceRequest("Camión Pereira"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceResponse>(JsonOptions);
        Assert.Equal(deviceId, body!.DeviceId);
        Assert.Equal("Camión Pereira", body.VehicleName);
    }

    [Fact]
    public async Task Rename_duplicate_name_returns_409()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var a = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var b = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await client.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(a));
        await client.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(b));
        await client.PatchAsJsonAsync($"/api/devices/{a}/name", new RenameDeviceRequest("Camión Norte"));

        using var conflict = await client.PatchAsJsonAsync(
            $"/api/devices/{b}/name",
            new RenameDeviceRequest("Camión Norte"));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Rename_unknown_device_returns_404()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var missing = Guid.Parse("66666666-6666-6666-6666-666666666666");

        using var response = await client.PatchAsJsonAsync(
            $"/api/devices/{missing}/name",
            new RenameDeviceRequest("Camión Fantasma"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Register_empty_device_id_returns_400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/devices/register",
            new RegisterDeviceRequest(Guid.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rename_invalid_name_returns_400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        await client.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(deviceId));

        using var response = await client.PatchAsJsonAsync(
            $"/api/devices/{deviceId}/name",
            new RenameDeviceRequest("X"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static DeviceApiWebApplicationFactory CreateFactory() => new();
}

public sealed class InMemoryDeviceRegistry : IDeviceRegistry
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

    public Task<FleetDevice> RegisterDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            throw new InvalidDeviceIdException("DeviceId is required.");

        while (true)
        {
            if (_byId.TryGetValue(deviceId, out var existing))
                return Task.FromResult(existing);

            var sequence = Interlocked.Increment(ref _sequence);
            var name = VehicleName.FormatAutomatic(sequence);
            var now = DateTimeOffset.UtcNow;
            var created = FleetDevice.Create(deviceId, name, now, now);

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
        CancellationToken cancellationToken = default)
    {
        if (!VehicleName.TryCreate(vehicleName, out var normalized, out var error))
            throw new InvalidVehicleNameException(error ?? "invalid");

        if (!_byId.TryGetValue(deviceId, out var current))
            throw new DeviceNotFoundException(deviceId);

        if (_nameOwners.TryGetValue(normalized!.Value, out var owner) && owner != deviceId)
            throw new VehicleNameConflictException(normalized.Value);

        var now = DateTimeOffset.UtcNow;
        var renamed = FleetDevice.Create(current.DeviceId, normalized.Value, current.CreatedAt, now);
        _nameOwners.TryRemove(current.VehicleName, out _);
        _nameOwners[normalized.Value] = deviceId;
        _byId[deviceId] = renamed;
        return Task.FromResult(renamed);
    }
}

public sealed class DeviceApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public InMemoryDeviceRegistry Registry { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Auth:Enabled", "false");
        builder.UseSetting("TimescaleDb:ConnectionString", "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet");
        builder.UseSetting("Kafka:BootstrapServers", "localhost:19092");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = "false",
                ["TimescaleDb:ConnectionString"] = "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet",
                ["Kafka:BootstrapServers"] = "localhost:19092",
                ["RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(descriptor);

            services.RemoveAll<IDeviceRegistry>();
            services.AddSingleton<IDeviceRegistry>(Registry);
        });
    }
}
