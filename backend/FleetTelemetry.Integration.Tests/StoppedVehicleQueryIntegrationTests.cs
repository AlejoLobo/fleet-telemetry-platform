using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using FleetTelemetry.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

public class StoppedVehicleQueryIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestDatabase _database = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options =>
            options.UseNpgsql(_database.ConnectionString));
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.Configure<StoppedVehicleQueryOptions>(options =>
        {
            options.LookbackHours = 48;
            options.VehicleFreshnessMinutes = 30;
            options.MaximumTelemetryGapMinutes = 60;
            options.StoppedSpeedThresholdKmh = 1;
        });
        services.AddScoped<IFleetOperationalQueryService, TimescaleFleetOperationalQueryService>();

        _services = services.BuildServiceProvider();
        await DatabaseInitializer.InitializeAsync(_services);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_seven_minutes_from_first_stopped_after_movement()
    {
        await ResetTelemetryAsync();
        var day = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        var moveAt = day.AddHours(10);
        var firstStopped = day.AddHours(10).AddMinutes(18);
        var lastStopped = day.AddHours(10).AddMinutes(25);
        _timeProvider.SetUtcNow(day.AddHours(10).AddMinutes(30));

        await SeedEventsAsync(
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-DUR-1"), moveAt, 45),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-DUR-1"), firstStopped, 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-DUR-1"), lastStopped, 0));

        using var scope = _services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();
        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(5));

        var vehicle = Assert.Single(stopped);
        Assert.Equal(firstStopped, vehicle.StoppedSince);
        Assert.Equal(lastStopped, vehicle.LastSeenAt);
        Assert.Equal(TimeSpan.FromMinutes(7), vehicle.StoppedDuration);
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_returns_multiple_vehicles_independently()
    {
        await ResetTelemetryAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedEventsAsync(
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-A"), now.AddMinutes(-50), 30),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-A"), now.AddMinutes(-40), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-A"), now.AddMinutes(-5), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-B"), now.AddMinutes(-70), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-B"), now.AddMinutes(-35), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-B"), now.AddMinutes(-4), 0));

        using var scope = _services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();
        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(2, stopped.Count);
        Assert.Contains(stopped, v => v.DeviceId == DeviceIdTestHelper.CreateDeterministicGuid("VH-A"));
        Assert.Contains(stopped, v => v.DeviceId == DeviceIdTestHelper.CreateDeterministicGuid("VH-B"));
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_continuous_sequence_after_last_movement()
    {
        await ResetTelemetryAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedEventsAsync(
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-STOP-1"), now.AddMinutes(-90), 40),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-STOP-1"), now.AddMinutes(-60), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-STOP-1"), now.AddMinutes(-30), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-STOP-1"), now.AddMinutes(-5), 0));

        using var scope = _services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();

        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(30));

        var vehicle = Assert.Single(stopped);
        Assert.Equal(DeviceIdTestHelper.CreateDeterministicGuid("VH-STOP-1"), vehicle.DeviceId);
        Assert.Equal(now.AddMinutes(-60), vehicle.StoppedSince);
        Assert.Equal(now.AddMinutes(-5), vehicle.LastSeenAt);
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_excludes_intermediate_movement()
    {
        await ResetTelemetryAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedEventsAsync(
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-MOVE-1"), now.AddMinutes(-80), 50),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-MOVE-1"), now.AddMinutes(-70), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-MOVE-1"), now.AddMinutes(-50), 25),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-MOVE-1"), now.AddMinutes(-20), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-MOVE-1"), now.AddMinutes(-5), 0));

        using var scope = _services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();

        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(10));

        var vehicle = Assert.Single(stopped);
        Assert.Equal(now.AddMinutes(-20), vehicle.StoppedSince);
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_excludes_stale_signal()
    {
        await ResetTelemetryAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedEventsAsync(
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-STALE-1"), now.AddHours(-3), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-STALE-1"), now.AddMinutes(-45), 0));

        using var scope = _services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();

        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(20));

        Assert.Empty(stopped);
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_excludes_large_telemetry_gap()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var tightTimeProvider = new FakeTimeProvider();
        tightTimeProvider.SetUtcNow(now);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options =>
            options.UseNpgsql(_database.ConnectionString));
        services.AddSingleton<TimeProvider>(tightTimeProvider);
        services.Configure<StoppedVehicleQueryOptions>(options =>
        {
            options.LookbackHours = 48;
            options.VehicleFreshnessMinutes = 30;
            options.MaximumTelemetryGapMinutes = 10;
            options.StoppedSpeedThresholdKmh = 1;
        });
        services.AddScoped<IFleetOperationalQueryService, TimescaleFleetOperationalQueryService>();
        using var provider = services.BuildServiceProvider();

        await SeedEventsInProviderAsync(provider,
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-GAP-1"), now.AddMinutes(-120), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-GAP-1"), now.AddMinutes(-5), 0));

        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();
        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(30));

        Assert.Empty(stopped);
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_includes_always_stopped_vehicle()
    {
        await ResetTelemetryAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedEventsAsync(
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-ALWAYS-1"), now.AddMinutes(-90), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-ALWAYS-1"), now.AddMinutes(-60), 0),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-ALWAYS-1"), now.AddMinutes(-10), 0));

        using var scope = _services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();

        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(45));

        var vehicle = Assert.Single(stopped);
        Assert.Equal(DeviceIdTestHelper.CreateDeterministicGuid("VH-ALWAYS-1"), vehicle.DeviceId);
        Assert.True(vehicle.StoppedDuration >= TimeSpan.FromMinutes(45));
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThan_marks_critical_zone()
    {
        await ResetTelemetryAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        // Coordenadas dentro de zona crítica de Bogotá (Centro).
        await SeedEventsAsync(
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-ZONE-1"), now.AddMinutes(-70), 0, 4.5986, -74.0758),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-ZONE-1"), now.AddMinutes(-40), 0, 4.5986, -74.0758),
            (DeviceIdTestHelper.CreateDeterministicGuid("VH-ZONE-1"), now.AddMinutes(-5), 0, 4.5986, -74.0758));

        using var scope = _services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IFleetOperationalQueryService>();

        var stopped = await query.GetVehiclesStoppedLongerThanAsync(TimeSpan.FromMinutes(30));

        var vehicle = Assert.Single(stopped);
        Assert.NotNull(vehicle.CriticalZoneName);
    }

    private async Task ResetTelemetryAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE telemetry_events");
    }

    private async Task SeedEventsInProviderAsync(
        IServiceProvider provider,
        params (Guid DeviceId, DateTimeOffset Timestamp, double SpeedKmh)[] events)
    {
        var mapped = events.Select(e => (e.DeviceId, e.Timestamp, e.SpeedKmh, 4.65, -74.08)).ToArray();
        await SeedEventsInProviderAsync(provider, mapped);
    }

    private async Task SeedEventsInProviderAsync(
        IServiceProvider provider,
        params (Guid DeviceId, DateTimeOffset Timestamp, double SpeedKmh, double Lat, double Lng)[] events)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        foreach (var item in events)
        {
            db.TelemetryEvents.Add(new TelemetryEventRecord
            {
                EventId = Guid.NewGuid(),
                VehicleId = item.DeviceId.ToString("D"),
                Timestamp = item.Timestamp,
                CapturedAt = item.Timestamp,
                Latitude = item.Lat,
                Longitude = item.Lng,
                SpeedKmh = item.SpeedKmh,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedEventsAsync(params (Guid DeviceId, DateTimeOffset Timestamp, double SpeedKmh, double Lat, double Lng)[] events)
    {
        await SeedEventsInProviderAsync(_services, events);
    }

    private async Task SeedEventsAsync(params (Guid DeviceId, DateTimeOffset Timestamp, double SpeedKmh)[] events)
    {
        await SeedEventsInProviderAsync(_services, events);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
