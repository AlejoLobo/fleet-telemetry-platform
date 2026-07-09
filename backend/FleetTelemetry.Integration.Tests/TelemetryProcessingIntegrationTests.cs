using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace FleetTelemetry.Integration.Tests;

// Pruebas de integración contra PostgreSQL/TimescaleDB real vía Testcontainers.
public class TelemetryProcessingIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("timescale/timescaledb:latest-pg16")
        .WithDatabase("fleet")
        .WithUsername("fleet")
        .WithPassword("fleet")
        .Build();

    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options =>
            options.UseNpgsql(_postgres.GetConnectionString()));

        services.AddScoped<ITelemetryProcessingUnitOfWork, TimescaleTelemetryProcessingUnitOfWork>();

        _services = services.BuildServiceProvider();

        await DatabaseInitializer.InitializeAsync(_services);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task ProcessAsync_persists_telemetry_and_generates_alerts()
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryEvent = CreateOverspeedEvent();

        var outcome = await uow.ProcessAsync(telemetryEvent);

        Assert.Equal(ProcessTelemetryOutcome.Processed, outcome);

        var stored = await db.TelemetryEvents.CountAsync(e => e.EventId == telemetryEvent.EventId);
        Assert.Equal(1, stored);

        var alerts = await db.FleetAlerts.CountAsync(a => a.VehicleId == telemetryEvent.VehicleId);
        Assert.True(alerts >= 1);
    }

    [Fact]
    public async Task ProcessAsync_is_idempotent_for_duplicate_event_id()
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryEvent = CreateNormalEvent();

        var first = await uow.ProcessAsync(telemetryEvent);
        var second = await uow.ProcessAsync(telemetryEvent);

        Assert.Equal(ProcessTelemetryOutcome.Processed, first);
        Assert.Equal(ProcessTelemetryOutcome.Duplicate, second);

        var stored = await db.TelemetryEvents.CountAsync(e => e.EventId == telemetryEvent.EventId);
        Assert.Equal(1, stored);
    }

    private static TelemetryEvent CreateOverspeedEvent() => new()
    {
        EventId = Guid.NewGuid(),
        VehicleId = "VH-INT-001",
        DriverId = "DRV-INT-001",
        Timestamp = DateTimeOffset.UtcNow,
        Latitude = 4.65,
        Longitude = -74.08,
        SpeedKmh = 130,
        FuelLevelPercent = 50,
        BatteryPercent = 90
    };

    private static TelemetryEvent CreateNormalEvent() => new()
    {
        EventId = Guid.NewGuid(),
        VehicleId = "VH-INT-002",
        Timestamp = DateTimeOffset.UtcNow,
        Latitude = 4.60,
        Longitude = -74.10,
        SpeedKmh = 45,
        FuelLevelPercent = 70,
        BatteryPercent = 85
    };
}
