using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

public class RateLimitingPolicyIntegrationTests
{
    [Fact]
    public async Task Health_routes_never_return_429_when_limiter_enabled()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1, telemetryLimit: 1);
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/health", "/health/live", "/health/ready" })
        {
            for (var i = 0; i < 20; i++)
            {
                using var response = await client.GetAsync(path);
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Health_admin_style_path_is_not_accidentally_exempt()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1, telemetryLimit: 100);
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/health-admin");
        using var second = await client.GetAsync("/health-admin");
        // Ruta inexistente aún pasa por el limiter global de IP.
        Assert.Contains(
            new[] { first.StatusCode, second.StatusCode },
            code => code == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Sse_stream_is_exempt_from_global_limiter()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1, telemetryLimit: 1);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);

        for (var i = 0; i < 10; i++)
        {
            try
            {
                using var response = await client.GetAsync("/api/events/stream", HttpCompletionOption.ResponseHeadersRead);
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
            catch (TaskCanceledException)
            {
                // El stream puede permanecer abierto; no debe haberse rechazado por 429.
            }
        }
    }

    [Fact]
    public async Task Sse_stream_test_path_is_not_accidentally_exempt()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1, telemetryLimit: 100);
        using var client = factory.CreateClient();

        var saw429 = false;
        for (var i = 0; i < 8; i++)
        {
            using var response = await client.GetAsync("/api/events/stream-test");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                saw429 = true;
                break;
            }
        }

        Assert.True(saw429);
    }

    [Fact]
    public async Task Post_telemetry_uses_ingest_policy()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1000, telemetryLimit: 2);
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");

        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceId)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceId)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostTelemetryAsync(client, deviceId)).StatusCode);
    }

    [Fact]
    public async Task Post_telemetry_batch_uses_same_partition()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1000, telemetryLimit: 2);
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");

        for (var i = 0; i < 2; i++)
        {
            using var ok = await PostBatchAsync(client, deviceId);
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var limited = await PostBatchAsync(client, deviceId);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Telemetry_admin_path_does_not_use_ingest_policy()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 2, telemetryLimit: 1000);
        using var client = factory.CreateClient();

        // Rutas admin caen en el limiter REST por IP, no en la cuota de ingesta.
        using var a = await client.PostAsync("/api/telemetry-admin", null);
        using var b = await client.PostAsync("/api/telemetry-admin", null);
        using var c = await client.PostAsync("/api/telemetry-admin", null);
        Assert.Contains(
            new[] { a.StatusCode, b.StatusCode, c.StatusCode },
            code => code == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Distinct_device_headers_use_separate_telemetry_partitions()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1000, telemetryLimit: 2);
        using var client = factory.CreateClient();
        var deviceA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var deviceB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceA)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceA)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostTelemetryAsync(client, deviceA)).StatusCode);

        // Desbordar A no debe afectar a B.
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceB)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceB)).StatusCode);
    }

    [Fact]
    public async Task Distinct_device_claims_with_same_sub_use_separate_partitions()
    {
        // Sin claim device_id: el encabezado define la partición pese a compartir sub.
        using var factory = CreateFactory(
            enabled: true,
            permitLimit: 1000,
            telemetryLimit: 1,
            claims: [new Claim("sub", "user-shared")]);
        using var client = factory.CreateClient();
        var deviceA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var deviceB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceA)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostTelemetryAsync(client, deviceA)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceB)).StatusCode);
    }

    [Fact]
    public async Task Device_claim_has_priority_over_sub()
    {
        var priorityDevice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        using var factory = CreateFactory(
            enabled: true,
            permitLimit: 1000,
            telemetryLimit: 1,
            claims: [new Claim("sub", "user-1"), new Claim("device_id", priorityDevice.ToString("D"))]);
        using var client = factory.CreateClient();

        // El claim device_id define la partición; el header debe coincidir con el payload.
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, priorityDevice)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostTelemetryAsync(client, priorityDevice)).StatusCode);
    }

    [Fact]
    public async Task Device_header_has_priority_over_sub_when_no_device_claim()
    {
        using var factory = CreateFactory(
            enabled: true,
            permitLimit: 1000,
            telemetryLimit: 1,
            claims: [new Claim("sub", "user-shared")]);
        using var client = factory.CreateClient();
        var deviceA = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff1");
        var deviceB = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff2");

        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceA)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostTelemetryAsync(client, deviceA)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceB)).StatusCode);
    }

    [Fact]
    public async Task Empty_or_invalid_device_header_uses_fallback()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1000, telemetryLimit: 1);
        using var client = factory.CreateClient();
        var payloadDevice = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Header vacío: se omite y la partición cae a IP.
        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, payloadDevice, headerDeviceId: "   ")).StatusCode);
        // Header inválido: TryNormalizeDeviceId falla → misma partición IP → 429 antes del guard.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostTelemetryAsync(client, payloadDevice, headerDeviceId: "too@@@")).StatusCode);

        using var longId = await PostTelemetryAsync(client, payloadDevice, headerDeviceId: new string('a', 200));
        Assert.Equal(HttpStatusCode.TooManyRequests, longId.StatusCode);
    }

    [Fact]
    public async Task Retry_After_is_present_and_numeric_on_429()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1000, telemetryLimit: 1);
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        Assert.Equal(HttpStatusCode.Accepted, (await PostTelemetryAsync(client, deviceId)).StatusCode);
        using var limited = await PostTelemetryAsync(client, deviceId);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        var body = await limited.Content.ReadFromJsonAsync<RateLimitBody>();
        Assert.NotNull(body);
        Assert.True(body!.RetryAfterSeconds >= 1);
    }

    [Fact]
    public async Task Development_disabled_limiter_does_not_return_429()
    {
        using var factory = CreateFactory(enabled: false, permitLimit: 1, telemetryLimit: 1);
        using var client = factory.CreateClient();
        var deviceId = Guid.Parse("12121212-1212-1212-1212-121212121212");

        for (var i = 0; i < 15; i++)
        {
            using var response = await client.GetAsync("/api/auth/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var ingest = await PostTelemetryAsync(client, deviceId);
            Assert.Equal(HttpStatusCode.Accepted, ingest.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> PostTelemetryAsync(
        HttpClient client,
        Guid deviceId,
        string? headerDeviceId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry")
        {
            Content = JsonContent.Create(CreateValidEvent(deviceId))
        };
        var headerValue = headerDeviceId ?? deviceId.ToString("D");
        if (!string.IsNullOrWhiteSpace(headerValue))
            request.Headers.TryAddWithoutValidation("X-Device-Id", headerValue);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostBatchAsync(HttpClient client, Guid deviceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/batch")
        {
            Content = JsonContent.Create(new TelemetryBatchRequest([CreateValidEvent(deviceId)]))
        };
        request.Headers.TryAddWithoutValidation("X-Device-Id", deviceId.ToString("D"));
        return await client.SendAsync(request);
    }

    private static TelemetryEventRequest CreateValidEvent(Guid deviceId) =>
        new(
            Guid.NewGuid(),
            deviceId,
            "DRV-001",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            40,
            80,
            90,
            "gps");

    private static RateLimitingWebApplicationFactory CreateFactory(
        bool enabled,
        int permitLimit,
        int telemetryLimit,
        Claim[]? claims = null)
    {
        Claim[]? resolved = claims;
        if (claims is { Length: > 0 })
        {
            resolved =
            [
                ..claims,
                new Claim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.TelemetryWrite),
            ];
        }

        return new()
        {
            RateLimitingEnabled = enabled,
            PermitLimit = permitLimit,
            TelemetryPermitLimit = telemetryLimit,
            TestClaims = resolved,
        };
    }

    private sealed record RateLimitBody(string Error, int RetryAfterSeconds);
}

public sealed class RateLimitingSpyPublisher : ITelemetryEventPublisher
{
    public Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class RateLimitingTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public RateLimitingTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = Context.RequestServices.GetRequiredService<RateLimitingClaimBag>().Claims;
        if (claims.Length == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class RateLimitingClaimBag
{
    public Claim[] Claims { get; init; } = [];
}

public sealed class RateLimitingWebApplicationFactory : WebApplicationFactory<Program>
{
    public bool RateLimitingEnabled { get; init; } = true;
    public int PermitLimit { get; init; } = 2;
    public int TelemetryPermitLimit { get; init; } = 2;
    public Claim[]? TestClaims { get; init; }
    public RateLimitingSpyPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Auth:Enabled", "false");
        builder.UseSetting("TimescaleDb:ConnectionString", "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet");
        builder.UseSetting("Kafka:BootstrapServers", "localhost:19092");
        builder.UseSetting("RateLimiting:Enabled", RateLimitingEnabled.ToString());
        builder.UseSetting("RateLimiting:PermitLimit", PermitLimit.ToString());
        builder.UseSetting("RateLimiting:WindowSeconds", "60");
        builder.UseSetting("RateLimiting:TelemetryPermitLimit", TelemetryPermitLimit.ToString());
        builder.UseSetting("RateLimiting:TelemetryWindowSeconds", "60");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = "false",
                ["TimescaleDb:ConnectionString"] = "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet",
                ["Kafka:BootstrapServers"] = "localhost:19092",
                ["RateLimiting:Enabled"] = RateLimitingEnabled.ToString(),
                ["RateLimiting:PermitLimit"] = PermitLimit.ToString(),
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:QueueLimit"] = "0",
                ["RateLimiting:TelemetryPermitLimit"] = TelemetryPermitLimit.ToString(),
                ["RateLimiting:TelemetryWindowSeconds"] = "60",
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(descriptor);

            services.RemoveAll<ITelemetryEventPublisher>();
            services.AddSingleton<ITelemetryEventPublisher>(Publisher);
            services.AddSingleton(new RateLimitingClaimBag { Claims = TestClaims ?? [] });
            // Auth se mantiene deshabilitado; inyectamos User solo para resolver la partición.
            services.AddSingleton<IStartupFilter, RateLimitingClaimsStartupFilter>();
        });
    }
}

public sealed class RateLimitingClaimsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var bag = context.RequestServices.GetService<RateLimitingClaimBag>();
                if (bag?.Claims is { Length: > 0 })
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(bag.Claims, authenticationType: "Test"));
                }

                await nextMiddleware();
            });
            next(app);
        };
    }
}
