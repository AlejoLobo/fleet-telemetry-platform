using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

// FT-004 tests 29-32: agregados SQL operativos y SseMode.
public class OpsAggregateIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestDatabase _database = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var services = new ServiceCollection();
        IntegrationTestServiceBootstrap.AddFleetTelemetryIntegrationServices(
            services,
            _database.ConnectionString,
            _timeProvider);

        _services = services.BuildServiceProvider();
        await DatabaseInitializer.InitializeAsync(_services);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GetFleetAggregateSnapshot_cuenta_total_y_activos_correctamente()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedFleetStatesAsync(
            ("VH-AGG-1", now.AddMinutes(-2)),
            ("VH-AGG-2", now.AddMinutes(-4)),
            ("VH-AGG-3", now.AddMinutes(-20)));

        using var scope = _services.CreateScope();
        var aggregateRepository = scope.ServiceProvider.GetRequiredService<IFleetStateAggregateRepository>();

        var snapshot = await aggregateRepository.GetFleetAggregateSnapshotAsync();

        Assert.Equal(3, snapshot.TotalVehicles);
        Assert.Equal(2, snapshot.ActiveVehicles);
        Assert.Equal(now.AddMinutes(-2), snapshot.LastTelemetryAt);
    }

    [Fact]
    public async Task CountOpenCriticalAlerts_cuenta_solo_criticas_abiertas()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        await SeedAlertsAsync(
            (Guid.Parse("11111111-1111-1111-1111-111111111111"), "VH-1", "critical", false),
            (Guid.Parse("22222222-2222-2222-2222-222222222222"), "VH-2", "critical", true),
            (Guid.Parse("33333333-3333-3333-3333-333333333333"), "VH-3", "warning", false),
            (Guid.Parse("44444444-4444-4444-4444-444444444444"), "VH-4", "CRITICAL", false));

        using var scope = _services.CreateScope();
        var aggregateRepository = scope.ServiceProvider.GetRequiredService<IFleetStateAggregateRepository>();

        var count = await aggregateRepository.CountOpenCriticalAlertsAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetSummaryAsync_expone_metricas_operativas_desde_sql()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedFleetStatesAsync(
            ("VH-OPS-1", now.AddMinutes(-1)),
            ("VH-OPS-2", now.AddMinutes(-30)));
        await SeedAlertsAsync(
            (Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "VH-OPS-1", "critical", false));

        using var scope = _services.CreateScope();
        var opsQuery = scope.ServiceProvider.GetRequiredService<IOpsQueryService>();

        var summary = await opsQuery.GetSummaryAsync();

        Assert.Equal(2, summary.TotalVehicles);
        Assert.Equal(1, summary.ActiveVehicles);
        Assert.Equal(1, summary.CriticalAlerts);
        Assert.Equal(now.AddMinutes(-1), summary.LastTelemetryAt);
        Assert.Equal("telemetry.raw", summary.TelemetryTopic);
        Assert.Equal("telemetry.dead-letter", summary.DeadLetterTopic);
    }

    [Fact]
    public async Task GetSummaryAsync_refleja_SseMode_kafka_push_y_polling()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var pollingScope = _services.CreateScope();
        var pollingOps = pollingScope.ServiceProvider.GetRequiredService<IOpsQueryService>();
        var pollingSummary = await pollingOps.GetSummaryAsync();
        Assert.Equal("polling", pollingSummary.SseMode);

        var kafkaServices = new ServiceCollection();
        IntegrationTestServiceBootstrap.AddFleetTelemetryIntegrationServices(
            kafkaServices,
            _database.ConnectionString,
            _timeProvider);
        kafkaServices.Configure<SseOptions>(options => options.Mode = SseDeliveryMode.KafkaPush);
        await using var kafkaProvider = kafkaServices.BuildServiceProvider();

        using var kafkaScope = kafkaProvider.CreateScope();
        var kafkaOps = kafkaScope.ServiceProvider.GetRequiredService<IOpsQueryService>();
        var kafkaSummary = await kafkaOps.GetSummaryAsync();

        Assert.Equal("kafka-push", kafkaSummary.SseMode);
    }

    private async Task SeedFleetStatesAsync(params (string VehicleId, DateTimeOffset LastTimestamp)[] states)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        foreach (var item in states)
        {
            db.FleetVehicleStates.Add(new FleetVehicleStateRecord
            {
                VehicleId = item.VehicleId,
                LastEventId = Guid.NewGuid(),
                LastTimestamp = item.LastTimestamp,
                Latitude = 4.65,
                Longitude = -74.08,
                SpeedKmh = 35,
                LocationSource = "gps",
                UpdatedAt = item.LastTimestamp,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedAlertsAsync(params (Guid AlertId, string VehicleId, string Severity, bool Acknowledged)[] alerts)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var createdAt = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

        foreach (var alert in alerts)
        {
            db.FleetAlerts.Add(new FleetAlertRecord
            {
                AlertId = alert.AlertId,
                VehicleId = alert.VehicleId,
                AlertType = "test",
                Severity = alert.Severity,
                Message = "test alert",
                CreatedAt = createdAt,
                IsAcknowledged = alert.Acknowledged,
            });
        }

        await db.SaveChangesAsync();
    }
}
