using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

// FT-004 tests 1-8: read model, UPSERT, out-of-order, backfill y rollback transaccional.
public class FleetVehicleStateIntegrationTests : IAsyncLifetime
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
    public async Task ProcessAsync_actualiza_read_model_fleet_vehicle_state()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryEvent = CreateEvent(
            vehicleId: "VH-READ-001",
            timestamp: new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
            speedKmh: 55,
            latitude: 4.65,
            longitude: -74.08);

        var outcome = await uow.ProcessAsync(telemetryEvent);

        Assert.Equal(ProcessTelemetryOutcome.Processed, outcome);

        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == telemetryEvent.VehicleId);
        Assert.Equal(telemetryEvent.EventId, state.LastEventId);
        Assert.Equal(telemetryEvent.Timestamp, state.LastTimestamp);
        Assert.Equal(telemetryEvent.SpeedKmh, state.SpeedKmh);
        Assert.Equal(telemetryEvent.Latitude, state.Latitude);
        Assert.Equal(telemetryEvent.Longitude, state.Longitude);
        Assert.Equal(telemetryEvent.LocationSource, state.LocationSource);
    }

    [Fact]
    public async Task ProcessAsync_upsert_reemplaza_estado_existente_del_mismo_vehiculo()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var vehicleId = "VH-UPSERT-001";
        var first = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero),
            speedKmh: 30,
            latitude: 4.60,
            longitude: -74.10);
        var second = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
            speedKmh: 80,
            latitude: 4.70,
            longitude: -74.05);

        await uow.ProcessAsync(first);
        await uow.ProcessAsync(second);

        var states = await db.FleetVehicleStates.Where(s => s.VehicleId == vehicleId).ToListAsync();
        Assert.Single(states);

        var state = states[0];
        Assert.Equal(second.EventId, state.LastEventId);
        Assert.Equal(second.Timestamp, state.LastTimestamp);
        Assert.Equal(second.SpeedKmh, state.SpeedKmh);
        Assert.Equal(second.Latitude, state.Latitude);
    }

    [Fact]
    public async Task ProcessAsync_evento_fuera_de_orden_no_revierte_estado()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var vehicleId = "VH-OOO-001";
        var newer = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero),
            speedKmh: 70,
            latitude: 4.71,
            longitude: -74.04);
        var older = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero),
            speedKmh: 20,
            latitude: 4.59,
            longitude: -74.11);

        await uow.ProcessAsync(newer);
        await uow.ProcessAsync(older);

        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == vehicleId);
        Assert.Equal(newer.EventId, state.LastEventId);
        Assert.Equal(newer.Timestamp, state.LastTimestamp);
        Assert.Equal(newer.SpeedKmh, state.SpeedKmh);
    }

    [Fact]
    public async Task ProcessAsync_mismo_timestamp_mayor_eventId_gana_desempate()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var vehicleId = "VH-TIE-001";
        var timestamp = new DateTimeOffset(2026, 7, 10, 10, 30, 0, TimeSpan.Zero);
        var lowerEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var higherEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var first = CreateEvent(vehicleId, timestamp, speedKmh: 40, latitude: 4.61, longitude: -74.09, eventId: lowerEventId);
        var second = CreateEvent(vehicleId, timestamp, speedKmh: 42, latitude: 4.62, longitude: -74.08, eventId: higherEventId);

        await uow.ProcessAsync(first);
        await uow.ProcessAsync(second);

        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == vehicleId);
        Assert.Equal(higherEventId, state.LastEventId);
        Assert.Equal(42, state.SpeedKmh);
    }

    [Fact]
    public async Task DatabaseInitializer_backfill_reconstruye_fleet_vehicle_state_desde_telemetry()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var baseTime = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        await SeedTelemetryOnlyAsync(
            ("VH-BF-001", baseTime.AddMinutes(10), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 30, 4.60, -74.10),
            ("VH-BF-001", baseTime.AddMinutes(30), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 50, 4.65, -74.08),
            ("VH-BF-002", baseTime.AddMinutes(20), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 15, 4.55, -74.12));

        await using var connection = new Npgsql.NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var resetCommand = connection.CreateCommand();
        resetCommand.CommandText = """
            TRUNCATE TABLE fleet_vehicle_state RESTART IDENTITY CASCADE;
            DELETE FROM schema_versions WHERE "Version" IN (2, 3);
            """;
        await resetCommand.ExecuteNonQueryAsync();

        await DatabaseInitializer.InitializeAsync(_services);

        using var verifyScope = _services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var state1 = await verifyDb.FleetVehicleStates.SingleAsync(s => s.VehicleId == "VH-BF-001");
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), state1.LastEventId);
        Assert.Equal(baseTime.AddMinutes(30), state1.LastTimestamp);
        Assert.Equal(50, state1.SpeedKmh);

        var state2 = await verifyDb.FleetVehicleStates.SingleAsync(s => s.VehicleId == "VH-BF-002");
        Assert.Equal(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), state2.LastEventId);
    }

    [Fact]
    public async Task ProcessAsync_duplicado_hace_rollback_y_no_corrompe_estado()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var telemetryEvent = CreateEvent(
            "VH-ROLL-001",
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            speedKmh: 60,
            latitude: 4.66,
            longitude: -74.07);

        await uow.ProcessAsync(telemetryEvent);

        var stateBefore = await db.FleetVehicleStates.AsNoTracking()
            .SingleAsync(s => s.VehicleId == telemetryEvent.VehicleId);
        var telemetryBefore = await db.TelemetryEvents.CountAsync();
        var processedBefore = await db.ProcessedEvents.CountAsync();
        var alertsBefore = await db.FleetAlerts.CountAsync();

        var duplicateOutcome = await uow.ProcessAsync(telemetryEvent);

        Assert.Equal(ProcessTelemetryOutcome.Duplicate, duplicateOutcome);

        var stateAfter = await db.FleetVehicleStates.AsNoTracking()
            .SingleAsync(s => s.VehicleId == telemetryEvent.VehicleId);
        Assert.Equal(stateBefore.LastEventId, stateAfter.LastEventId);
        Assert.Equal(stateBefore.LastTimestamp, stateAfter.LastTimestamp);
        Assert.Equal(stateBefore.SpeedKmh, stateAfter.SpeedKmh);
        Assert.Equal(telemetryBefore, await db.TelemetryEvents.CountAsync());
        Assert.Equal(processedBefore, await db.ProcessedEvents.CountAsync());
        Assert.Equal(alertsBefore, await db.FleetAlerts.CountAsync());
    }

    [Fact]
    public async Task GetVehicleStatusAsync_retorna_estado_desde_read_model()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var now = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(
            "VH-STATUS-001",
            now.AddMinutes(-2),
            speedKmh: 45,
            latitude: 4.64,
            longitude: -74.09);

        await uow.ProcessAsync(telemetryEvent);

        var status = await fleetQuery.GetVehicleStatusAsync(telemetryEvent.VehicleId);

        Assert.NotNull(status);
        Assert.Equal(telemetryEvent.VehicleId, status.VehicleId);
        Assert.Equal("online", status.Status);
        Assert.Equal(telemetryEvent.Timestamp, status.LastSeenAt);
        Assert.Equal(telemetryEvent.SpeedKmh, status.LastSpeedKmh);
        Assert.Equal(telemetryEvent.Latitude, status.LastLatitude);
        Assert.Equal(telemetryEvent.Longitude, status.LastLongitude);
    }

    [Fact]
    public async Task GetVehicleStatusAsync_calcula_heading_con_punto_previo()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var vehicleId = "VH-HDG-001";
        var first = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
            speedKmh: 40,
            latitude: 4.60,
            longitude: -74.10);
        var second = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 10, 5, 0, TimeSpan.Zero),
            speedKmh: 42,
            latitude: 4.65,
            longitude: -74.08);

        await uow.ProcessAsync(first);
        await uow.ProcessAsync(second);

        var status = await fleetQuery.GetVehicleStatusAsync(vehicleId);

        Assert.NotNull(status);
        Assert.NotNull(status.LastHeadingDegrees);
        Assert.InRange(status.LastHeadingDegrees.Value, 0, 360);
    }

    private static TelemetryEvent CreateEvent(
        string vehicleId,
        DateTimeOffset timestamp,
        double speedKmh,
        double latitude,
        double longitude,
        Guid? eventId = null) =>
        TelemetryEvent.Create(
            eventId ?? Guid.NewGuid(),
            vehicleId,
            "DRV-INT-001",
            timestamp,
            latitude,
            longitude,
            speedKmh,
            70,
            85);

    private async Task SeedTelemetryOnlyAsync(
        params (string VehicleId, DateTimeOffset Timestamp, Guid EventId, double SpeedKmh, double Lat, double Lng)[] events)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        foreach (var item in events)
        {
            db.TelemetryEvents.Add(new Infrastructure.Persistence.Entities.TelemetryEventRecord
            {
                EventId = item.EventId,
                VehicleId = item.VehicleId,
                Timestamp = item.Timestamp,
                CapturedAt = item.Timestamp,
                Latitude = item.Lat,
                Longitude = item.Lng,
                SpeedKmh = item.SpeedKmh,
                LocationSource = "gps",
            });
        }

        await db.SaveChangesAsync();
    }
}
