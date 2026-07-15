using System.Net;
using System.Net.Http.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Application.Tests;

/// <summary>
/// La ingesta de telemetría no debe quedar sujeta a cuota por IP:
/// muchos dispositivos envían cada ~3 s y pueden compartir NAT.
/// </summary>
public class TelemetryIngestRateLimitIntegrationTests
{
    [Fact]
    public async Task Ingest_with_tight_rate_limit_still_accepts_many_posts()
    {
        using var factory = new TelemetryIngestRateLimitWebApplicationFactory
        {
            RateLimitingEnabled = true,
            PermitLimit = 2,
            WindowSeconds = 60,
        };
        using var client = factory.CreateClient();

        for (var i = 0; i < 40; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/telemetry", CreateValidEvent($"VH-{i:D3}"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal(40, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Ingest_batch_with_tight_rate_limit_still_accepts()
    {
        using var factory = new TelemetryIngestRateLimitWebApplicationFactory
        {
            RateLimitingEnabled = true,
            PermitLimit = 1,
            WindowSeconds = 60,
        };
        using var client = factory.CreateClient();

        for (var i = 0; i < 15; i++)
        {
            var batch = new TelemetryBatchRequest([CreateValidEvent($"VH-{i:D3}")]);
            using var response = await client.PostAsJsonAsync("/api/telemetry/batch", batch);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal(15, factory.Publisher.PublishCount);
    }

    [Fact]
    public async Task Non_ingest_route_can_still_be_rate_limited()
    {
        using var factory = new TelemetryIngestRateLimitWebApplicationFactory
        {
            RateLimitingEnabled = true,
            PermitLimit = 2,
            WindowSeconds = 60,
        };
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            using var response = await client.GetAsync("/api/auth/status");
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        using var ingest = await client.PostAsJsonAsync("/api/telemetry", CreateValidEvent("VH-999"));
        Assert.Equal(HttpStatusCode.Accepted, ingest.StatusCode);
    }

    private static TelemetryEventRequest CreateValidEvent(string vehicleId) =>
        new(
            Guid.NewGuid(),
            vehicleId,
            "DRV-LOAD",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            40,
            80,
            90,
            "gps");
}

public sealed class TelemetryIngestRateLimitWebApplicationFactory : WebApplicationFactory<Program>
{
    public bool RateLimitingEnabled { get; init; } = true;
    public int PermitLimit { get; init; } = 2;
    public int WindowSeconds { get; init; } = 60;
    public SpyTelemetryEventPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Auth:Enabled", "false");
        builder.UseSetting("TimescaleDb:ConnectionString", "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet");
        builder.UseSetting("Kafka:BootstrapServers", "localhost:19092");
        builder.UseSetting("RateLimiting:Enabled", RateLimitingEnabled.ToString());
        builder.UseSetting("RateLimiting:PermitLimit", PermitLimit.ToString());
        builder.UseSetting("RateLimiting:WindowSeconds", WindowSeconds.ToString());
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = "false",
                ["TimescaleDb:ConnectionString"] = "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet",
                ["Kafka:BootstrapServers"] = "localhost:19092",
                ["RateLimiting:Enabled"] = RateLimitingEnabled.ToString(),
                ["RateLimiting:PermitLimit"] = PermitLimit.ToString(),
                ["RateLimiting:WindowSeconds"] = WindowSeconds.ToString(),
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
