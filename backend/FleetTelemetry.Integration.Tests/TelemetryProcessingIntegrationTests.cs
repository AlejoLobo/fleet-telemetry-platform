using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Integration.Tests;

// Pruebas de integración contra TimescaleDB real (Testcontainers o Compose local).
public class TelemetryProcessingIntegrationTests : IAsyncLifetime
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
    public async Task ProcessAsync_same_event_id_does_not_duplicate_telemetry_events()
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryEvent = CreateNormalEvent();

        var first = await uow.ProcessAsync(telemetryEvent);
        var second = await uow.ProcessAsync(telemetryEvent);

        Assert.Equal(ProcessTelemetryOutcome.Processed, first);
        Assert.Equal(ProcessTelemetryOutcome.Duplicate, second);

        Assert.Equal(1, await db.TelemetryEvents.CountAsync(e => e.EventId == telemetryEvent.EventId));
        Assert.Equal(1, await db.ProcessedEvents.CountAsync(e => e.EventId == telemetryEvent.EventId));
    }

    [Fact]
    public async Task ProcessAsync_transactional_consistency_across_processed_events_telemetry_events_and_fleet_alerts()
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryEvent = CreateOverspeedEvent();

        var outcome = await uow.ProcessAsync(telemetryEvent);

        Assert.Equal(ProcessTelemetryOutcome.Processed, outcome);
        Assert.Equal(1, await db.ProcessedEvents.CountAsync(e => e.EventId == telemetryEvent.EventId));
        Assert.Equal(1, await db.TelemetryEvents.CountAsync(e => e.EventId == telemetryEvent.EventId));

        var storedEvent = await db.TelemetryEvents.SingleAsync(e => e.EventId == telemetryEvent.EventId);
        Assert.Equal(telemetryEvent.VehicleId, storedEvent.VehicleId);
        Assert.Equal(telemetryEvent.SpeedKmh, storedEvent.SpeedKmh);

        var alerts = await db.FleetAlerts
            .Where(a => a.VehicleId == telemetryEvent.VehicleId)
            .ToListAsync();
        Assert.NotEmpty(alerts);
        Assert.All(alerts, alert => Assert.Equal(telemetryEvent.VehicleId, alert.VehicleId));
    }

    [Fact]
    public async Task ProcessAsync_overspeed_generates_critical_alert()
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryEvent = CreateOverspeedEvent();

        await uow.ProcessAsync(telemetryEvent);

        var overspeedAlert = await db.FleetAlerts
            .SingleAsync(a => a.VehicleId == telemetryEvent.VehicleId && a.AlertType == "overspeed");

        Assert.Equal("critical", overspeedAlert.Severity);
        Assert.Contains(telemetryEvent.VehicleId, overspeedAlert.Message);
    }

    [Fact]
    public async Task Invalid_payload_deserialization_does_not_persist_valid_event()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryBefore = await db.TelemetryEvents.CountAsync();
        var processedBefore = await db.ProcessedEvents.CountAsync();
        var alertsBefore = await db.FleetAlerts.CountAsync();

        var invalidPayloads = new[]
        {
            string.Empty,
            "{ not valid json }",
            "null"
        };

        foreach (var payload in invalidPayloads)
        {
            var exception = Assert.ThrowsAny<Exception>(() =>
                TelemetryEventJsonSerializer.Deserialize(payload));

            Assert.True(
                exception is TelemetryKafkaContractException or System.Text.Json.JsonException,
                $"Expected deserialization failure, got {exception.GetType().Name}");
        }

        // JSON parcial deserializa, pero falla validación de dominio (Worker → reason invalid_payload).
        const string partialJson = """{"vehicleId":"VH-001"}""";
        const string invalidDomainReason = "invalid_domain";
        var partialException = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize(partialJson));
        Assert.Contains("EventId", partialException.Message);
        Assert.Equal("invalid_domain", partialException.ErrorCode);
        Assert.Equal("invalid_domain", invalidDomainReason);

        // El Worker no llama a ProcessAsync cuando Validate falla; no se persiste nada.
        Assert.Equal(telemetryBefore, await db.TelemetryEvents.CountAsync());
        Assert.Equal(processedBefore, await db.ProcessedEvents.CountAsync());
        Assert.Equal(alertsBefore, await db.FleetAlerts.CountAsync());
    }

    private static TelemetryEvent CreateOverspeedEvent() =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            $"VH-INT-{Guid.NewGuid():N}"[..16],
            "DRV-INT-001",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            130,
            50,
            90);

    private static TelemetryEvent CreateNormalEvent() =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            $"VH-INT-{Guid.NewGuid():N}"[..16],
            null,
            DateTimeOffset.UtcNow,
            4.60,
            -74.10,
            45,
            70,
            85);
}
