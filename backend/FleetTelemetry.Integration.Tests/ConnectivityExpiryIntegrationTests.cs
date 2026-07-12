using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
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
        Assert.NotNull(payload?.StatusEvaluatedAt);
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
        Assert.NotNull(payload?.StatusEvaluatedAt);
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

    private async Task<DateTimeOffset?> GetWatermarkAsync()
    {
        using var scope = _services.CreateScope();
        var watermarkRepository = scope.ServiceProvider.GetRequiredService<IFleetConnectivityWatermarkRepository>();
        return await watermarkRepository.GetPreviousOnlineThresholdAsync();
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

// FT-004: paginación keyset y watermark durable del expirador.
public class ConnectivityExpiryPagingIntegrationTests : IAsyncLifetime
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
            configureSseOptions: options => options.ConnectivityExpiryBatchSize = 2,
            configurePublisher: _publisher);

        _services = services.BuildServiceProvider();
        await DatabaseInitializer.InitializeAsync(_services);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Mas_de_batchSize_vehiculos_expiran_sin_perderse()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        for (var index = 1; index <= 5; index++)
        {
            await ProcessAsync(CreateEvent(
                $"VH-PAGE-{index:D3}",
                now.AddMinutes(-2),
                index * 10,
                4.60 + index * 0.01,
                -74.08));
        }

        _publisher.Reset();
        _timeProvider.SetUtcNow(now.AddMinutes(6));

        var published = await PublishExpiryAsync();

        Assert.Equal(5, published);
        Assert.Equal(5, _publisher.VehicleUpdates.Count);
        Assert.Equal(
            new[] { "VH-PAGE-001", "VH-PAGE-002", "VH-PAGE-003", "VH-PAGE-004", "VH-PAGE-005" },
            _publisher.VehicleUpdates.Select(update => update.VehicleId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task Vehiculos_con_mismo_timestamp_cruzan_varias_paginas()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 19, 0, 0, TimeSpan.Zero);
        var sharedTimestamp = now.AddMinutes(-2);
        _timeProvider.SetUtcNow(now);

        await ProcessAsync(CreateEvent("VH-SAME-C", sharedTimestamp, 10, 4.61, -74.08));
        await ProcessAsync(CreateEvent("VH-SAME-A", sharedTimestamp, 20, 4.62, -74.07));
        await ProcessAsync(CreateEvent("VH-SAME-B", sharedTimestamp, 30, 4.63, -74.06));

        _publisher.Reset();
        _timeProvider.SetUtcNow(now.AddMinutes(6));

        var published = await PublishExpiryAsync();

        Assert.Equal(3, published);
        Assert.Equal(
            new[] { "VH-SAME-A", "VH-SAME-B", "VH-SAME-C" },
            _publisher.VehicleUpdates.Select(update => update.VehicleId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task Fallo_del_publisher_no_adelanta_watermark()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 20, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await ProcessAsync(CreateEvent("VH-FAIL-WM", now.AddMinutes(-2), 0, 4.64, -74.05));
        _publisher.Reset();
        _timeProvider.SetUtcNow(now.AddMinutes(6));

        _publisher.FailNextVehiclePublish = true;
        await Assert.ThrowsAsync<InvalidOperationException>(PublishExpiryAsync);
        Assert.Null(await GetWatermarkAsync());

        _publisher.Reset();
        var published = await PublishExpiryAsync();

        Assert.Equal(1, published);
        Assert.NotNull(await GetWatermarkAsync());
    }

    [Fact]
    public async Task Reintento_no_duplica_los_ya_publicados()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 21, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await ProcessAsync(CreateEvent("VH-RETRY-A", now.AddMinutes(-2), 10, 4.65, -74.04));
        await ProcessAsync(CreateEvent("VH-RETRY-B", now.AddMinutes(-2), 20, 4.66, -74.03));

        _publisher.Reset();
        _timeProvider.SetUtcNow(now.AddMinutes(6));
        _publisher.FailOnVehicleId = "VH-RETRY-B";

        await Assert.ThrowsAsync<InvalidOperationException>(PublishExpiryAsync);
        Assert.Single(_publisher.VehicleUpdates);
        Assert.Equal("VH-RETRY-A", _publisher.VehicleUpdates[0].VehicleId);
        Assert.Null(await GetWatermarkAsync());

        _publisher.FailOnVehicleId = null;
        var published = await PublishExpiryAsync();

        Assert.Equal(1, published);
        Assert.Equal(2, _publisher.VehicleUpdates.Count);
        Assert.Equal(1, _publisher.VehicleUpdates.Count(update => update.VehicleId == "VH-RETRY-A"));
        Assert.Equal(1, _publisher.VehicleUpdates.Count(update => update.VehicleId == "VH-RETRY-B"));
    }

    [Fact]
    public async Task Reinicio_despues_del_lookback_recupera_expiraciones_pendientes()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 22, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await ProcessAsync(CreateEvent("VH-RESTART", now.AddMinutes(-2), 15, 4.67, -74.02));
        _publisher.Reset();

        // Más allá del lookback de 90s; el watermark durable debe seguir cubriendo la ventana.
        _timeProvider.SetUtcNow(now.AddHours(3));

        var published = await PublishExpiryAsync();

        Assert.Equal(1, published);
        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);
        Assert.Equal(VehicleConnectivityStatus.Offline, payload?.Status);
        Assert.Equal("VH-RESTART", _publisher.VehicleUpdates[0].VehicleId);
    }

    [Fact]
    public async Task Ningun_vehiculo_queda_online_indefinidamente()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 23, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var vehicleIds = new[] { "VH-NO-STALE-1", "VH-NO-STALE-2", "VH-NO-STALE-3" };
        foreach (var vehicleId in vehicleIds)
        {
            await ProcessAsync(CreateEvent(vehicleId, now.AddMinutes(-2), 25, 4.68, -74.01));
        }

        _publisher.Reset();
        _timeProvider.SetUtcNow(now.AddMinutes(6));
        await PublishExpiryAsync();

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        foreach (var vehicleId in vehicleIds)
        {
            var status = await fleetQuery.GetVehicleStatusAsync(vehicleId);
            Assert.Equal(VehicleConnectivityStatus.Offline, status?.Status);
        }

        Assert.All(
            _publisher.VehicleUpdates.Select(update =>
                _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(update.PayloadJson)),
            payload => Assert.Equal(VehicleConnectivityStatus.Offline, payload?.Status));
    }

    private async Task ResetAsync()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        _publisher.Reset();
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

    private async Task<DateTimeOffset?> GetWatermarkAsync()
    {
        using var scope = _services.CreateScope();
        var watermarkRepository = scope.ServiceProvider.GetRequiredService<IFleetConnectivityWatermarkRepository>();
        return await watermarkRepository.GetPreviousOnlineThresholdAsync();
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
