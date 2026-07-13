using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace FleetTelemetry.Application.Tests;

public class SseStreamAuthenticationIntegrationTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Stream_auth_disabled_allows_connection_without_token()
    {
        using var factory = CreateFactory(authEnabled: false);
        using var client = factory.CreateClient();

        await using var stream = await OpenSseStreamAsync(client, factory.GetStreamPath());
        var firstChunk = await ReadChunkAsync(stream, TimeSpan.FromSeconds(5));

        Assert.Contains("event: connected", firstChunk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_auth_enabled_without_token_returns_401()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(factory.GetStreamPath());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_auth_enabled_with_invalid_token_returns_401()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        var response = await client.GetAsync(factory.GetStreamPath());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_auth_enabled_with_valid_token_and_fleet_read_returns_connected()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await using var stream = await OpenSseStreamAsync(client, factory.GetStreamPath());
        var firstChunk = await ReadChunkAsync(stream, TimeSpan.FromSeconds(5));

        Assert.Contains("event: connected", firstChunk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_auth_enabled_with_valid_token_without_fleet_read_returns_403()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = CreateJwtWithoutFleetRead(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(factory.GetStreamPath());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static SseStreamAuthenticationWebApplicationFactory CreateFactory(bool authEnabled) =>
        new() { AuthEnabled = authEnabled };

    private static async Task<string> LoginAsync(HttpClient client, SseStreamAuthenticationWebApplicationFactory factory)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(factory.DemoUsername, factory.DemoPassword));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        return body.Token;
    }

    private static string CreateJwtWithoutFleetRead(SseStreamAuthenticationWebApplicationFactory factory)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-secret-with-32-chars-min"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, factory.DemoUsername),
            new Claim(ClaimTypes.Role, "operator"),
            new Claim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.TelemetryWrite),
        };

        var token = new JwtSecurityToken(
            issuer: "fleet-telemetry",
            audience: "fleet-clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<Stream> OpenSseStreamAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStreamAsync();
    }

    private static async Task<string> ReadChunkAsync(Stream stream, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[2048];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}

public sealed class SseStreamAuthenticationWebApplicationFactory : WebApplicationFactory<Program>
{
    public bool AuthEnabled { get; init; } = true;

    public string DemoUsername => "admin";
    public string DemoPassword => "admin123";

    public string GetStreamPath() => "/api/events/stream";

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
            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var descriptor in hosted)
                services.Remove(descriptor);

            // Sin hosted KafkaPush: marcar Ready para no bloquear tests de autenticación SSE.
            var readinessDescriptors = services
                .Where(d => d.ServiceType == typeof(FleetTelemetry.Infrastructure.Realtime.IFleetKafkaPushReadiness))
                .ToList();
            foreach (var descriptor in readinessDescriptors)
                services.Remove(descriptor);

            var readiness = new FleetTelemetry.Infrastructure.Realtime.FleetKafkaPushReadiness();
            readiness.EstablishInitialPosition(0);
            readiness.MarkReady();
            services.AddSingleton<FleetTelemetry.Infrastructure.Realtime.IFleetKafkaPushReadiness>(readiness);
        });
    }
}
