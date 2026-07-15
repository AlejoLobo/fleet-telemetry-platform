using System.Net;
using System.Net.Http.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Application.Tests;

public class RateLimitingPolicyIntegrationTests
{
    [Fact]
    public async Task Health_never_returns_429_when_limiter_enabled()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1, telemetryLimit: 1);
        using var client = factory.CreateClient();

        for (var i = 0; i < 20; i++)
        {
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
    }

    [Fact]
    public async Task Rest_route_can_be_rate_limited()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 2, telemetryLimit: 100);
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            using var response = await client.GetAsync("/api/auth/status");
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.True(response.Headers.RetryAfter?.Delta?.TotalSeconds > 0
                    || int.TryParse(response.Headers.RetryAfter?.ToString(), out _));
                var body = await response.Content.ReadFromJsonAsync<RateLimitBody>();
                Assert.NotNull(body);
                Assert.True(body!.RetryAfterSeconds >= 1);
            }
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Distinct_device_headers_use_separate_telemetry_partitions()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1000, telemetryLimit: 2);
        using var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var a = await PostTelemetryAsync(client, "device-a");
            Assert.Equal(HttpStatusCode.Accepted, a.StatusCode);
            using var b = await PostTelemetryAsync(client, "device-b");
            Assert.Equal(HttpStatusCode.Accepted, b.StatusCode);
        }

        using var overflowA = await PostTelemetryAsync(client, "device-a");
        Assert.Equal(HttpStatusCode.TooManyRequests, overflowA.StatusCode);
        Assert.NotNull(overflowA.Headers.RetryAfter);

        using var stillOkB = await PostTelemetryAsync(client, "device-b");
        Assert.Equal(HttpStatusCode.TooManyRequests, stillOkB.StatusCode);
    }

    [Fact]
    public async Task Batch_uses_same_device_partition()
    {
        using var factory = CreateFactory(enabled: true, permitLimit: 1000, telemetryLimit: 2);
        using var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var ok = await PostBatchAsync(client, "device-batch");
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var limited = await PostBatchAsync(client, "device-batch");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Development_disabled_limiter_does_not_return_429()
    {
        using var factory = CreateFactory(enabled: false, permitLimit: 1, telemetryLimit: 1);
        using var client = factory.CreateClient();

        for (var i = 0; i < 15; i++)
        {
            using var response = await client.GetAsync("/api/auth/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var ingest = await PostTelemetryAsync(client, "dev-device");
            Assert.Equal(HttpStatusCode.Accepted, ingest.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> PostTelemetryAsync(HttpClient client, string deviceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry")
        {
            Content = JsonContent.Create(CreateValidEvent())
        };
        request.Headers.TryAddWithoutValidation("X-Device-Id", deviceId);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostBatchAsync(HttpClient client, string deviceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/batch")
        {
            Content = JsonContent.Create(new TelemetryBatchRequest([CreateValidEvent()]))
        };
        request.Headers.TryAddWithoutValidation("X-Device-Id", deviceId);
        return await client.SendAsync(request);
    }

    private static TelemetryEventRequest CreateValidEvent() =>
        new(
            Guid.NewGuid(),
            "VH-001",
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
        int telemetryLimit) =>
        new()
        {
            RateLimitingEnabled = enabled,
            PermitLimit = permitLimit,
            TelemetryPermitLimit = telemetryLimit,
        };

    private sealed record RateLimitBody(string Error, int RetryAfterSeconds);
}

public sealed class RateLimitingSpyPublisher : ITelemetryEventPublisher
{
    public Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class RateLimitingWebApplicationFactory : WebApplicationFactory<Program>
{
    public bool RateLimitingEnabled { get; init; } = true;
    public int PermitLimit { get; init; } = 2;
    public int TelemetryPermitLimit { get; init; } = 2;
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
        });
    }
}
