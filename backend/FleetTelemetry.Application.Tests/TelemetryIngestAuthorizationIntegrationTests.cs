using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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
using Microsoft.IdentityModel.Tokens;

namespace FleetTelemetry.Application.Tests;

public class TelemetryIngestAuthorizationIntegrationTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Batch_auth_disabled_without_token_returns_202()
    {
        using var factory = CreateFactory(authEnabled: false);
        using var client = factory.CreateClient();
        using var response = await PostBatchAsync(client, CreateValidBatch());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(factory.Publisher.PublishCount > 0);
    }

    [Fact]
    public async Task Batch_auth_enabled_without_token_returns_401()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        using var response = await PostBatchAsync(client, CreateValidBatch());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Batch_auth_enabled_with_invalid_token_returns_401()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");
        using var response = await PostBatchAsync(client, CreateValidBatch());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Batch_auth_enabled_with_valid_token_returns_202()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await PostBatchAsync(client, CreateValidBatch());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(factory.Publisher.PublishCount > 0);
    }

    [Fact]
    public async Task Batch_auth_enabled_with_token_without_telemetry_write_returns_403()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = CreateJwtWithoutTelemetryWrite(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await PostBatchAsync(client, CreateValidBatch());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Single_auth_disabled_without_token_returns_202()
    {
        using var factory = CreateFactory(authEnabled: false);
        using var client = factory.CreateClient();
        using var response = await PostSingleAsync(client, CreateValidEvent());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Single_auth_enabled_without_token_returns_401()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        using var response = await PostSingleAsync(client, CreateValidEvent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Single_auth_enabled_with_invalid_token_returns_401()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");
        using var response = await PostSingleAsync(client, CreateValidEvent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Single_auth_enabled_with_token_without_telemetry_write_returns_403()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = CreateJwtWithoutTelemetryWrite(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await PostSingleAsync(client, CreateValidEvent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Single_auth_enabled_with_valid_token_returns_202()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await PostSingleAsync(client, CreateValidEvent());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, factory.Publisher.PublishCount);
    }

    private static TelemetryIngestAuthorizationWebApplicationFactory CreateFactory(bool authEnabled) =>
        new() { AuthEnabled = authEnabled };

    private static TelemetryBatchRequest CreateValidBatch() =>
        new([CreateValidEvent()]);

    private static TelemetryEventRequest CreateValidEvent() =>
        new(
            Guid.NewGuid(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "DRV-001",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            45.5,
            80,
            90,
            "gps");

    private static async Task<HttpResponseMessage> PostBatchAsync(HttpClient client, TelemetryBatchRequest batch) =>
        await client.PostAsJsonAsync("/api/telemetry/batch", batch);

    private static async Task<HttpResponseMessage> PostSingleAsync(HttpClient client, TelemetryEventRequest request) =>
        await client.PostAsJsonAsync("/api/telemetry", request);

    private static async Task<string> LoginAsync(HttpClient client, TelemetryIngestAuthorizationWebApplicationFactory factory)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(factory.DemoUsername, factory.DemoPassword));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        return body!.Token;
    }

    private static string CreateJwtWithoutTelemetryWrite(TelemetryIngestAuthorizationWebApplicationFactory factory)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-secret-with-32-chars-min"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, factory.DemoUsername),
            new Claim(ClaimTypes.Role, "operator"),
            new Claim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.FleetRead),
        };

        var token = new JwtSecurityToken(
            issuer: "fleet-telemetry",
            audience: "fleet-clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class SpyTelemetryEventPublisher : ITelemetryEventPublisher
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

public sealed class TelemetryIngestAuthorizationWebApplicationFactory : WebApplicationFactory<Program>
{
    public bool AuthEnabled { get; init; } = true;
    public string DemoUsername => "admin";
    public string DemoPassword => "admin123";
    public SpyTelemetryEventPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Auth:Enabled", AuthEnabled.ToString());
        builder.UseSetting("Auth:JwtSecret", "integration-test-secret-with-32-chars-min");
        builder.UseSetting("Auth:JwtIssuer", "fleet-telemetry");
        builder.UseSetting("Auth:JwtAudience", "fleet-clients");
        builder.UseSetting("Auth:DemoUsername", DemoUsername);
        builder.UseSetting("Auth:DemoPassword", DemoPassword);
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
        });
    }
}
