using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

// FT-004: conectividad online/offline consistente entre REST, polling y KafkaPush.
public class VehicleConnectivityIntegrationTests : IAsyncLifetime
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
    public async Task Evento_reciente_velocidad_cero_es_online_en_REST_y_realtime()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(
            "VH-CONN-ZERO",
            now.AddMinutes(-2),
            speedKmh: 0,
            latitude: 4.65,
            longitude: -74.08);

        await ProcessAsync(telemetryEvent);

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();
        var status = await fleetQuery.GetVehicleStatusAsync(telemetryEvent.VehicleId);
        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.NotNull(status);
        Assert.NotNull(payload);
        Assert.Equal(VehicleConnectivityStatus.Online, status.Status);
        Assert.Equal(VehicleConnectivityStatus.Online, payload.Status);
    }

    [Fact]
    public async Task Evento_reciente_en_movimiento_es_online_en_REST_y_realtime()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(
            "VH-CONN-MOVE",
            now.AddMinutes(-1),
            speedKmh: 72,
            latitude: 4.66,
            longitude: -74.07);

        await ProcessAsync(telemetryEvent);

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();
        var status = await fleetQuery.GetVehicleStatusAsync(telemetryEvent.VehicleId);
        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.Equal(VehicleConnectivityStatus.Online, status?.Status);
        Assert.Equal(VehicleConnectivityStatus.Online, payload?.Status);
    }

    [Fact]
    public async Task Evento_antiguo_es_offline_en_REST_y_realtime()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(
            "VH-CONN-OLD",
            now.AddMinutes(-30),
            speedKmh: 40,
            latitude: 4.64,
            longitude: -74.09);

        await ProcessAsync(telemetryEvent);

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();
        var status = await fleetQuery.GetVehicleStatusAsync(telemetryEvent.VehicleId);
        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.Equal(VehicleConnectivityStatus.Offline, status?.Status);
        Assert.Equal(VehicleConnectivityStatus.Offline, payload?.Status);
    }

    [Fact]
    public async Task Estado_REST_polling_y_KafkaPush_es_identico()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 15, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(
            "VH-CONN-PARITY",
            now.AddMinutes(-3),
            speedKmh: 0,
            latitude: 4.67,
            longitude: -74.06);

        await ProcessAsync(telemetryEvent);

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();
        var restStatus = await fleetQuery.GetVehicleStatusAsync(telemetryEvent.VehicleId);
        var fleetPage = await fleetQuery.GetFleetPageAsync(pageSize: 10, cursor: null);
        var pollingStatus = fleetPage.Items.Single(v => v.VehicleId == telemetryEvent.VehicleId);
        var realtimePayload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.Equal(restStatus?.Status, pollingStatus.Status);
        Assert.Equal(restStatus?.Status, realtimePayload?.Status);
        Assert.Equal(VehicleConnectivityStatus.Online, restStatus?.Status);
    }

    [Fact]
    public async Task El_estado_DB_y_el_payload_realtime_comparan_tambien_Status()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 16, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(
            "VH-CONN-STATUS",
            now.AddMinutes(-4),
            speedKmh: 15,
            latitude: 4.68,
            longitude: -74.05);

        await ProcessAsync(telemetryEvent);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == telemetryEvent.VehicleId);
        var status = await fleetQuery.GetVehicleStatusAsync(telemetryEvent.VehicleId);
        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.NotNull(payload);
        Assert.Equal(state.LastTimestamp, payload.LastSeenAt);
        Assert.Equal(status?.Status, payload.Status);
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
        double longitude) =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            vehicleId,
            "DRV-INT-001",
            timestamp,
            latitude,
            longitude,
            speedKmh,
            70,
            85);
}
