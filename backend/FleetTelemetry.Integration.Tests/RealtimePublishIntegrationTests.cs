using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

// FT-004: publicación realtime condicionada por UPSERT de fleet_vehicle_state.
public class RealtimePublishIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestDatabase _database = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFleetRealtimePublisher _publisher = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var services = new ServiceCollection();
        IntegrationTestServiceBootstrap.AddFleetTelemetryIntegrationServices(
            services,
            _database.ConnectionString,
            _timeProvider,
            configurePublisher: _publisher);

        _services = services.BuildServiceProvider();
        await DatabaseInitializer.InitializeAsync(_services);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Evento_nuevo_publica_vehicle_update()
    {
        await ResetAsync();
        _publisher.Reset();

        var telemetryEvent = CreateEvent(
            "VH-RT-001",
            new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
            speedKmh: 55,
            latitude: 4.65,
            longitude: -74.08);

        await ProcessAsync(telemetryEvent);

        Assert.Single(_publisher.VehicleUpdates);
        Assert.Equal(telemetryEvent.VehicleId, _publisher.VehicleUpdates[0].VehicleId);
    }

    [Fact]
    public async Task Vehicle_update_usa_Timestamp_del_evento()
    {
        await ResetAsync();
        _publisher.Reset();

        var timestamp = new DateTimeOffset(2026, 7, 10, 11, 30, 0, TimeSpan.Zero);
        var telemetryEvent = CreateEvent("VH-RT-002", timestamp, 40, 4.61, -74.09);

        await ProcessAsync(telemetryEvent);

        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.NotNull(payload);
        Assert.Equal(timestamp, payload.LastSeenAt);
    }

    [Fact]
    public async Task Evento_fuera_de_orden_no_publica_vehicle_update()
    {
        await ResetAsync();
        _publisher.Reset();

        var vehicleId = "VH-RT-OOO";
        var newer = CreateEvent(vehicleId, new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero), 70, 4.71, -74.04);
        var older = CreateEvent(vehicleId, new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero), 20, 4.59, -74.11);

        await ProcessAsync(newer);
        _publisher.Reset();
        await ProcessAsync(older);

        Assert.Empty(_publisher.VehicleUpdates);
    }

    [Fact]
    public async Task Timestamp_igual_EventId_menor_no_publica_vehicle_update()
    {
        await ResetAsync();
        _publisher.Reset();

        var vehicleId = "VH-RT-TIE-LOW";
        var timestamp = new DateTimeOffset(2026, 7, 10, 10, 30, 0, TimeSpan.Zero);
        var higherEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var lowerEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var first = CreateEvent(vehicleId, timestamp, 40, 4.61, -74.09, higherEventId);
        var second = CreateEvent(vehicleId, timestamp, 35, 4.60, -74.10, lowerEventId);

        await ProcessAsync(first);
        _publisher.Reset();
        await ProcessAsync(second);

        Assert.Empty(_publisher.VehicleUpdates);
    }

    [Fact]
    public async Task Timestamp_igual_EventId_mayor_publica_vehicle_update()
    {
        await ResetAsync();
        _publisher.Reset();

        var vehicleId = "VH-RT-TIE-HIGH";
        var timestamp = new DateTimeOffset(2026, 7, 10, 10, 30, 0, TimeSpan.Zero);
        var lowerEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var higherEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var first = CreateEvent(vehicleId, timestamp, 40, 4.61, -74.09, lowerEventId);
        var second = CreateEvent(vehicleId, timestamp, 42, 4.62, -74.08, higherEventId);

        await ProcessAsync(first);
        _publisher.Reset();
        await ProcessAsync(second);

        Assert.Single(_publisher.VehicleUpdates);

        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);
        Assert.Equal(42, payload?.LastSpeedKmh);
    }

    [Fact]
    public async Task El_estado_DB_y_el_payload_realtime_coinciden()
    {
        await ResetAsync();
        _publisher.Reset();

        var telemetryEvent = CreateEvent(
            "VH-RT-MATCH",
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            speedKmh: 88,
            latitude: 4.67,
            longitude: -74.06);

        await ProcessAsync(telemetryEvent);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == telemetryEvent.VehicleId);

        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.NotNull(payload);
        Assert.Equal(state.LastTimestamp, payload.LastSeenAt);
        Assert.Equal(state.SpeedKmh, payload.LastSpeedKmh);
        Assert.Equal(state.Latitude, payload.LastLatitude);
        Assert.Equal(state.Longitude, payload.LastLongitude);
        Assert.Equal(state.LocationSource, payload.LastLocationSource);
    }

    [Fact]
    public async Task Evento_atrasado_con_alerta_no_regresa_vehiculo()
    {
        await ResetAsync();
        _publisher.Reset();

        var vehicleId = "VH-RT-ALERT";
        var newer = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero),
            speedKmh: 50,
            latitude: 4.70,
            longitude: -74.05,
            fuelLevelPercent: 70,
            batteryPercent: 85);
        var olderWithAlert = CreateEvent(
            vehicleId,
            new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero),
            speedKmh: 130,
            latitude: 4.59,
            longitude: -74.11,
            fuelLevelPercent: 70,
            batteryPercent: 85);

        await ProcessAsync(newer);
        _publisher.Reset();
        await ProcessAsync(olderWithAlert);

        Assert.Empty(_publisher.VehicleUpdates);
        Assert.NotEmpty(_publisher.AlertPayloads);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == vehicleId);
        Assert.Equal(newer.EventId, state.LastEventId);
        Assert.Equal(newer.Timestamp, state.LastTimestamp);
        Assert.Equal(newer.SpeedKmh, state.SpeedKmh);
    }

    private async Task ResetAsync()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        _publisher.Reset();
    }

    private async Task ProcessAsync(TelemetryEvent telemetryEvent)
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        await uow.ProcessAsync(telemetryEvent);
    }

    private static TelemetryEvent CreateEvent(
        string vehicleId,
        DateTimeOffset timestamp,
        double speedKmh,
        double latitude,
        double longitude,
        Guid? eventId = null,
        double fuelLevelPercent = 70,
        double batteryPercent = 85) =>
        TelemetryEvent.Create(
            eventId ?? Guid.NewGuid(),
            vehicleId,
            "DRV-INT-001",
            timestamp,
            latitude,
            longitude,
            speedKmh,
            fuelLevelPercent,
            batteryPercent);
}
