using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Domain.ValueObjects;
using Microsoft.AspNetCore.Builder;
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

    [Fact]
    public async Task Register_claim_and_payload_match_returns_ok()
    {
        var deviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa01");
        using var factory = CreateFactory(claims: [new Claim("device_id", deviceId.ToString("D"))]);
        using var client = factory.CreateClient();

        using var response = await PostRegisterAsync(client, deviceId, deviceId.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_claim_differs_from_payload_returns_403()
    {
        var payload = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa02");
        var claim = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb02");
        using var factory = CreateFactory(claims: [new Claim("device_id", claim.ToString("D"))]);
        using var client = factory.CreateClient();

        using var response = await PostRegisterAsync(client, payload, payload.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Register_invalid_claim_returns_403()
    {
        var payload = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa03");
        using var factory = CreateFactory(claims: [new Claim("device_id", "not-a-guid")]);
        using var client = factory.CreateClient();

        using var response = await PostRegisterAsync(client, payload, payload.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Register_header_differs_from_payload_returns_400()
    {
        var payload = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa04");
        var header = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb04");
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostRegisterAsync(client, payload, header.ToString("D"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rename_route_differs_from_header_returns_400()
    {
        var deviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa05");
        var other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb05");
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        await PostRegisterAsync(client, deviceId, deviceId.ToString("D"));

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/devices/{deviceId}/name")
        {
            Content = JsonContent.Create(new RenameDeviceRequest("Camión Sur"))
        };
        request.Headers.TryAddWithoutValidation("X-Device-Id", other.ToString("D"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rename_claim_for_another_device_returns_403()
    {
        var deviceA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa06");
        var deviceB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb06");
        using var factory = CreateFactory(claims: [new Claim("device_id", deviceA.ToString("D"))]);
        using var client = factory.CreateClient();

        // Registro previo sin claim conflictivo: el claim se aplica a todas las requests;
        // registramos B con un cliente sin claim usando registry compartido fuera del HTTP.
        await factory.Registry.RegisterDeviceAsync(deviceA);
        await factory.Registry.RegisterDeviceAsync(deviceB);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/devices/{deviceB}/name")
        {
            Content = JsonContent.Create(new RenameDeviceRequest("Nombre Ajeno"))
        };
        request.Headers.TryAddWithoutValidation("X-Device-Id", deviceB.ToString("D"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Auth_disabled_register_and_rename_still_work_with_matching_header()
    {
        var deviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa07");
        using var factory = CreateFactory(authEnabled: false);
        using var client = factory.CreateClient();

        using var register = await PostRegisterAsync(client, deviceId, deviceId.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        using var renameRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/devices/{deviceId}/name")
        {
            Content = JsonContent.Create(new RenameDeviceRequest("Unidad Libre"))
        };
        renameRequest.Headers.TryAddWithoutValidation("X-Device-Id", deviceId.ToString("D"));
        using var rename = await client.SendAsync(renameRequest);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    private static DeviceApiWebApplicationFactory CreateFactory(
        bool authEnabled = false,
        Claim[]? claims = null) =>
        new()
        {
            AuthEnabled = authEnabled,
            TestClaims = claims,
        };

    private static async Task<HttpResponseMessage> PostRegisterAsync(
        HttpClient client,
        Guid deviceId,
        string? headerDeviceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/register")
        {
            Content = JsonContent.Create(new RegisterDeviceRequest(deviceId))
        };
        if (!string.IsNullOrWhiteSpace(headerDeviceId))
            request.Headers.TryAddWithoutValidation("X-Device-Id", headerDeviceId);
        return await client.SendAsync(request);
    }
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
    public bool AuthEnabled { get; init; }
    public Claim[]? TestClaims { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Auth:Enabled", AuthEnabled.ToString());
        builder.UseSetting("TimescaleDb:ConnectionString", "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet");
        builder.UseSetting("Kafka:BootstrapServers", "localhost:19092");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = AuthEnabled.ToString(),
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
            services.AddSingleton(new DeviceEndpointClaimBag { Claims = TestClaims ?? [] });
            services.AddSingleton<IStartupFilter, DeviceEndpointClaimsStartupFilter>();
        });
    }
}

public sealed class DeviceEndpointClaimBag
{
    public Claim[] Claims { get; init; } = [];
}

public sealed class DeviceEndpointClaimsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var bag = context.RequestServices.GetService<DeviceEndpointClaimBag>();
                if (bag?.Claims is { Length: > 0 })
                {
                    context.User = new ClaimsPrincipal(
                        new ClaimsIdentity(bag.Claims, authenticationType: "Test"));
                }

                await nextMiddleware();
            });
            next(app);
        };
    }
}
