using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

// FT-004: transición online→offline observable sin telemetría nueva.
public class ConnectivityExpiryIntegrationTests : IAsyncLifetime
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
    public async Task Evento_reciente_publica_online()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent("VH-EXP-ON", now.AddMinutes(-2), 0, 4.65, -74.08);
        await ProcessAsync(telemetryEvent);

        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.Equal(VehicleConnectivityStatus.Online, payload?.Status);
        Assert.Equal(telemetryEvent.EventId, payload?.LastEventId);
    }

    [Fact]
    public async Task Avanzar_reloj_mas_alla_del_umbral_publica_offline_sin_evento_nuevo()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent("VH-EXP-OFF", now.AddMinutes(-2), 0, 4.65, -74.08);
        await ProcessAsync(telemetryEvent);
        _publisher.Reset();

        _timeProvider.SetUtcNow(now.AddMinutes(1));
        await PublishExpiryAsync();
        Assert.Empty(_publisher.VehicleUpdates);

        _timeProvider.SetUtcNow(now.AddMinutes(6));
        var published = await PublishExpiryAsync();

        Assert.Equal(1, published);
        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);
        Assert.Equal(VehicleConnectivityStatus.Offline, payload?.Status);
        Assert.Equal(telemetryEvent.EventId, payload?.LastEventId);
    }

    [Fact]
    public async Task No_publica_offline_repetidamente()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 15, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent("VH-EXP-NOREP", now.AddMinutes(-2), 10, 4.66, -74.07);
        await ProcessAsync(telemetryEvent);
        _publisher.Reset();

        _timeProvider.SetUtcNow(now.AddMinutes(1));
        await PublishExpiryAsync();

        _timeProvider.SetUtcNow(now.AddMinutes(6));
        var first = await PublishExpiryAsync();
        var second = await PublishExpiryAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(_publisher.VehicleUpdates);
    }

    [Fact]
    public async Task Evento_nuevo_despues_de_offline_regresa_a_online()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 16, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var firstEvent = CreateEvent("VH-EXP-BACK", now.AddMinutes(-2), 0, 4.67, -74.06);
        await ProcessAsync(firstEvent);
        _publisher.Reset();

        _timeProvider.SetUtcNow(now.AddMinutes(1));
        await PublishExpiryAsync();

        _timeProvider.SetUtcNow(now.AddMinutes(6));
        await PublishExpiryAsync();
        _publisher.Reset();

        var secondEvent = CreateEvent(
            "VH-EXP-BACK",
            now.AddMinutes(5),
            45,
            4.68,
            -74.05,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        await ProcessAsync(secondEvent);

        Assert.Single(_publisher.VehicleUpdates);
        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);
        Assert.Equal(VehicleConnectivityStatus.Online, payload?.Status);
        Assert.Equal(secondEvent.EventId, payload?.LastEventId);
    }

    [Fact]
    public async Task REST_y_dashboard_coinciden_despues_de_expirar()
    {
        await ResetAsync();
        _publisher.Reset();

        var now = new DateTimeOffset(2026, 7, 10, 17, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent("VH-EXP-PARITY", now.AddMinutes(-2), 5, 4.69, -74.04);
        await ProcessAsync(telemetryEvent);
        _publisher.Reset();

        _timeProvider.SetUtcNow(now.AddMinutes(1));
        await PublishExpiryAsync();

        _timeProvider.SetUtcNow(now.AddMinutes(6));
        await PublishExpiryAsync();

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();
        var restStatus = await fleetQuery.GetVehicleStatusAsync(telemetryEvent.VehicleId);
        var realtimePayload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);

        Assert.Equal(VehicleConnectivityStatus.Offline, restStatus?.Status);
        Assert.Equal(VehicleConnectivityStatus.Offline, realtimePayload?.Status);
        Assert.Equal(restStatus?.Status, realtimePayload?.Status);
    }

    private async Task ResetAsync()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        _publisher.Reset();

        using var scope = _services.CreateScope();
        var expiryState = scope.ServiceProvider.GetRequiredService<FleetConnectivityExpiryState>();
        expiryState.PreviousOnlineThreshold = null;
    }

    private async Task<int> PublishExpiryAsync()
    {
        using var scope = _services.CreateScope();
        var expiryService = scope.ServiceProvider.GetRequiredService<IFleetConnectivityExpiryService>();
        return await expiryService.PublishOfflineTransitionsAsync();
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
}
