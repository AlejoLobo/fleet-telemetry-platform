using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using FleetTelemetry.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Integration.Tests;

public class AlertStreamCursorIntegrationTests : IAsyncLifetime
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
        services.AddScoped<IAlertRepository, TimescaleAlertRepository>();

        _services = services.BuildServiceProvider();
        await DatabaseInitializer.InitializeAsync(_services);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GetOpenAlertsAfterCursor_returns_alerts_in_deterministic_order()
    {
        await ResetAlertsAsync();
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        var id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var id3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await SeedAlertsAsync(
            (id2, baseTime, "VH-002"),
            (id1, baseTime, "VH-001"),
            (id3, baseTime.AddMinutes(1), "VH-003"));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var upperBound = baseTime.AddMinutes(5);

        var batch = await repository.GetOpenAlertsAfterCursorAsync(
            AlertStreamCursor.Origin,
            upperBound,
            limit: 10);

        Assert.Equal(3, batch.Count);
        Assert.Equal(id1, batch[0].AlertId);
        Assert.Equal(id2, batch[1].AlertId);
        Assert.Equal(id3, batch[2].AlertId);
    }

    [Fact]
    public async Task GetOpenAlertsAfterCursor_paginates_without_duplicates()
    {
        await ResetAlertsAsync();
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(1, 5)
            .Select(i => Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-{i:D12}"))
            .ToArray();

        for (var i = 0; i < ids.Length; i++)
        {
            await SeedAlertsAsync((ids[i], baseTime.AddMinutes(i), $"VH-{i}"));
        }

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var upperBound = baseTime.AddHours(1);
        var cursor = AlertStreamCursor.Origin;
        var collected = new List<Guid>();

        while (true)
        {
            var batch = await repository.GetOpenAlertsAfterCursorAsync(cursor, upperBound, limit: 2);
            if (batch.Count == 0) break;

            foreach (var alert in batch)
            {
                collected.Add(alert.AlertId);
                cursor = AlertStreamCursor.FromAlert(alert.CreatedAt, alert.AlertId);
            }

            if (batch.Count < 2) break;
        }

        Assert.Equal(ids.Length, collected.Count);
        Assert.Equal(ids, collected);
    }

    [Fact]
    public async Task GetOpenAlertsAfterCursor_respects_upper_bound_captured_before_late_insert()
    {
        await ResetAlertsAsync();
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        var earlyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var lateId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await SeedAlertsAsync((earlyId, baseTime, "VH-EARLY"));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var upperBound = baseTime.AddSeconds(30);

        await SeedAlertsAsync((lateId, baseTime.AddMinutes(1), "VH-LATE"));

        var batch = await repository.GetOpenAlertsAfterCursorAsync(
            AlertStreamCursor.Origin,
            upperBound,
            limit: 10);

        Assert.Single(batch);
        Assert.Equal(earlyId, batch[0].AlertId);
    }

    [Fact]
    public async Task GetOpenAlertsAfterCursor_continues_from_last_cursor_without_duplicates()
    {
        await ResetAlertsAsync();
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        var id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await SeedAlertsAsync(
            (id1, baseTime, "VH-001"),
            (id2, baseTime.AddMinutes(1), "VH-002"));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var upperBound = baseTime.AddMinutes(5);
        var cursor = AlertStreamCursor.FromAlert(baseTime, id1);

        var batch = await repository.GetOpenAlertsAfterCursorAsync(cursor, upperBound, limit: 10);

        var alert = Assert.Single(batch);
        Assert.Equal(id2, alert.AlertId);
    }

    private async Task ResetAlertsAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fleet_alerts");
    }

    private async Task SeedAlertsAsync(params (Guid AlertId, DateTimeOffset CreatedAt, string VehicleId)[] alerts)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        foreach (var alert in alerts)
        {
            db.FleetAlerts.Add(new FleetAlertRecord
            {
                AlertId = alert.AlertId,
                VehicleId = alert.VehicleId,
                AlertType = "test",
                Severity = "warning",
                Message = "test",
                CreatedAt = alert.CreatedAt,
                IsAcknowledged = false,
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }
}
