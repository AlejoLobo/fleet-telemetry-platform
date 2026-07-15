using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Application.Tests;

/// <summary>
/// Emisión HTTP real de tokens de dispositivo y uso en register/ingesta/rename.
/// </summary>
public class DeviceTokenAuthIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Device_token_endpoint_emits_jwt_with_device_claims()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/device-token",
            new DeviceTokenRequest(DeviceA, factory.DemoUsername, factory.DemoPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(DeviceA, body!.DeviceId);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
        Assert.True(body.ExpiresInMinutes > 0);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.Token);
        Assert.Contains(jwt.Claims, c => c.Type == AuthorizationPermissions.DeviceIdClaimType && c.Value == DeviceA.ToString("D"));
        Assert.Contains(jwt.Claims, c => c.Type == AuthorizationPermissions.ClaimType && c.Value == AuthorizationPermissions.TelemetryWrite);
        Assert.Contains(jwt.Claims, c =>
            (c.Type == ClaimTypes.Role || c.Type == "role") && c.Value == "device");
    }

    [Fact]
    public async Task Device_token_rejects_empty_device_id()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/device-token",
            new DeviceTokenRequest(Guid.Empty, factory.DemoUsername, factory.DemoPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Device_token_rejects_invalid_credentials()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/device-token",
            new DeviceTokenRequest(DeviceA, "nope", "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Device_token_works_for_register_ingest_batch_and_rename()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await IssueDeviceTokenAsync(client, factory, DeviceA);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var registerReq = new HttpRequestMessage(HttpMethod.Post, "/api/devices/register")
        {
            Content = JsonContent.Create(new RegisterDeviceRequest(DeviceA)),
        };
        registerReq.Headers.TryAddWithoutValidation("X-Device-Id", DeviceA.ToString("D"));
        using var register = await client.SendAsync(registerReq);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        using var singleReq = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry")
        {
            Content = JsonContent.Create(CreateEvent(DeviceA)),
        };
        singleReq.Headers.TryAddWithoutValidation("X-Device-Id", DeviceA.ToString("D"));
        using var single = await client.SendAsync(singleReq);
        Assert.Equal(HttpStatusCode.Accepted, single.StatusCode);

        using var batchReq = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/batch")
        {
            Content = JsonContent.Create(new TelemetryBatchRequest([CreateEvent(DeviceA), CreateEvent(DeviceA)])),
        };
        batchReq.Headers.TryAddWithoutValidation("X-Device-Id", DeviceA.ToString("D"));
        using var batch = await client.SendAsync(batchReq);
        Assert.Equal(HttpStatusCode.Accepted, batch.StatusCode);

        using var renameReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/devices/{DeviceA}/name")
        {
            Content = JsonContent.Create(new RenameDeviceRequest("Unidad A")),
        };
        renameReq.Headers.TryAddWithoutValidation("X-Device-Id", DeviceA.ToString("D"));
        using var rename = await client.SendAsync(renameReq);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task Device_token_A_cannot_act_as_device_B()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var tokenA = await IssueDeviceTokenAsync(client, factory, DeviceA);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        using var registerReq = new HttpRequestMessage(HttpMethod.Post, "/api/devices/register")
        {
            Content = JsonContent.Create(new RegisterDeviceRequest(DeviceB)),
        };
        registerReq.Headers.TryAddWithoutValidation("X-Device-Id", DeviceB.ToString("D"));
        using var register = await client.SendAsync(registerReq);
        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);

        using var ingestReq = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry")
        {
            Content = JsonContent.Create(CreateEvent(DeviceB)),
        };
        ingestReq.Headers.TryAddWithoutValidation("X-Device-Id", DeviceA.ToString("D"));
        using var ingestAsB = await client.SendAsync(ingestReq);
        Assert.True(
            ingestAsB.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
            $"Unexpected status {ingestAsB.StatusCode}");
    }

    [Fact]
    public async Task Operator_login_cannot_publish_telemetry()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory.DemoUsername, factory.DemoPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsJsonAsync("/api/telemetry", CreateEvent(DeviceA));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_login_has_device_manage_can_rename_but_not_publish()
    {
        using var factory = CreateFactory(authEnabled: true, withAdmin: true);
        using var client = factory.CreateClient();

        var deviceToken = await IssueDeviceTokenAsync(client, factory, DeviceA);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        using var registerReq = new HttpRequestMessage(HttpMethod.Post, "/api/devices/register")
        {
            Content = JsonContent.Create(new RegisterDeviceRequest(DeviceA)),
        };
        registerReq.Headers.TryAddWithoutValidation("X-Device-Id", DeviceA.ToString("D"));
        using var register = await client.SendAsync(registerReq);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var adminToken = await LoginAsync(client, factory.AdminUsername, factory.AdminPassword);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(adminToken);
        Assert.Contains(jwt.Claims, c =>
            c.Type == AuthorizationPermissions.ClaimType
            && c.Value == AuthorizationPermissions.DeviceManage);
        Assert.DoesNotContain(jwt.Claims, c =>
            c.Type == AuthorizationPermissions.ClaimType
            && c.Value == AuthorizationPermissions.TelemetryWrite);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var rename = await client.PatchAsJsonAsync(
            $"/api/devices/{DeviceA}/name",
            new RenameDeviceRequest("Renombrado Admin"));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        using var ingest = await client.PostAsJsonAsync("/api/telemetry", CreateEvent(DeviceA));
        Assert.Equal(HttpStatusCode.Forbidden, ingest.StatusCode);
    }

    [Fact]
    public async Task Operator_login_does_not_include_device_manage()
    {
        using var factory = CreateFactory(authEnabled: true, withAdmin: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory.DemoUsername, factory.DemoPassword);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.DoesNotContain(jwt.Claims, c =>
            c.Type == AuthorizationPermissions.ClaimType
            && c.Value == AuthorizationPermissions.DeviceManage);
    }

    private static async Task<string> IssueDeviceTokenAsync(
        HttpClient client,
        DeviceTokenWebApplicationFactory factory,
        Guid deviceId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/device-token",
            new DeviceTokenRequest(deviceId, factory.DemoUsername, factory.DemoPassword));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(JsonOptions);
        return body!.Token;
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        return body!.Token;
    }

    private static TelemetryEventRequest CreateEvent(Guid deviceId) =>
        new(
            Guid.NewGuid(),
            deviceId,
            "DRV-001",
            DateTimeOffset.UtcNow,
            4.71,
            -74.07,
            42,
            55,
            80,
            "gps");

    private static DeviceTokenWebApplicationFactory CreateFactory(bool authEnabled, bool withAdmin = false) =>
        new() { AuthEnabled = authEnabled, WithAdmin = withAdmin };
}

public sealed class DeviceTokenWebApplicationFactory : WebApplicationFactory<Program>
{
    public bool AuthEnabled { get; init; }
    public bool WithAdmin { get; init; }
    public string DemoUsername => "admin";
    public string DemoPassword => "admin123";
    public string AdminUsername => "fleet-admin";
    public string AdminPassword => "admin-secret-123";
    public DeviceTokenSpyPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Auth:Enabled", AuthEnabled.ToString());
        builder.UseSetting("Auth:JwtSecret", "integration-test-secret-with-32-chars-min");
        builder.UseSetting("Auth:JwtIssuer", "fleet-telemetry");
        builder.UseSetting("Auth:JwtAudience", "fleet-clients");
        builder.UseSetting("Auth:DemoUsername", DemoUsername);
        builder.UseSetting("Auth:DemoPassword", DemoPassword);
        builder.UseSetting("Auth:AdminUsername", WithAdmin ? AdminUsername : "");
        builder.UseSetting("Auth:AdminPassword", WithAdmin ? AdminPassword : "");
        builder.UseSetting("TimescaleDb:ConnectionString", "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet");
        builder.UseSetting("Kafka:BootstrapServers", "localhost:19092");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = AuthEnabled.ToString(),
                ["Auth:JwtSecret"] = "integration-test-secret-with-32-chars-min",
                ["Auth:JwtIssuer"] = "fleet-telemetry",
                ["Auth:JwtAudience"] = "fleet-clients",
                ["Auth:DemoUsername"] = DemoUsername,
                ["Auth:DemoPassword"] = DemoPassword,
                ["Auth:AdminUsername"] = WithAdmin ? AdminUsername : "",
                ["Auth:AdminPassword"] = WithAdmin ? AdminPassword : "",
                ["TimescaleDb:ConnectionString"] = "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet",
                ["Kafka:BootstrapServers"] = "localhost:19092",
                ["RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(descriptor);

            services.RemoveAll<ITelemetryEventPublisher>();
            services.AddSingleton<ITelemetryEventPublisher>(Publisher);
            services.RemoveAll<IDeviceRegistry>();
            services.AddSingleton<IDeviceRegistry>(new InMemoryDeviceRegistry());
        });
    }
}

public sealed class DeviceTokenSpyPublisher : ITelemetryEventPublisher
{
    public int PublishCount { get; private set; }

    public Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        PublishCount++;
        return Task.CompletedTask;
    }

    public Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default)
    {
        PublishCount += events.Count();
        return Task.CompletedTask;
    }
}
