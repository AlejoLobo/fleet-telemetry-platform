using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

// FT-004: marcadores durables online/offline en el UoW y expirador.
public class ConnectivityPublishTrackerIntegrationTests : IAsyncLifetime
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
    public async Task Evento_ya_expirado_se_publica_offline_una_sola_vez()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(DeviceIdTestHelper.CreateDeterministicGuid("VH-TRK-OLD"), now.AddMinutes(-30), 0, 4.65, -74.08);
        await ProcessAsync(telemetryEvent);

        Assert.Single(_publisher.VehicleUpdates);
        var ingestPayload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);
        Assert.Equal(VehicleConnectivityStatus.Offline, ingestPayload?.Status);

        _publisher.Reset();
        _timeProvider.SetUtcNow(now.AddMinutes(6));

        var published = await PublishExpiryAsync();

        Assert.Equal(0, published);
        Assert.Empty(_publisher.VehicleUpdates);
    }

    [Fact]
    public async Task Evento_offline_no_limpia_su_marcador()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 15, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(DeviceIdTestHelper.CreateDeterministicGuid("VH-TRK-KEEP"), now.AddMinutes(-30), 0, 4.66, -74.07);
        await ProcessAsync(telemetryEvent);

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            var marker = await db.FleetOfflinePublishMarkers
                .SingleOrDefaultAsync(marker => marker.VehicleId == telemetryEvent.DeviceIdStorage);
            Assert.NotNull(marker);
            Assert.Equal(telemetryEvent.EventId, marker.LastEventId);
        }

        var duplicateOutcome = await ProcessAsync(telemetryEvent);
        Assert.Equal(ProcessTelemetryOutcome.Duplicate, duplicateOutcome);

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            var marker = await db.FleetOfflinePublishMarkers
                .SingleOrDefaultAsync(marker => marker.VehicleId == telemetryEvent.DeviceIdStorage);
            Assert.NotNull(marker);
            Assert.Equal(telemetryEvent.EventId, marker.LastEventId);
        }
    }

    [Fact]
    public async Task Evento_online_nuevo_limpia_marcador_anterior()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 16, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var offlineEvent = CreateEvent(DeviceIdTestHelper.CreateDeterministicGuid("VH-TRK-CLEAR"), now.AddMinutes(-30), 0, 4.67, -74.06);
        await ProcessAsync(offlineEvent);

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.NotEmpty(await db.FleetOfflinePublishMarkers
                .Where(marker => marker.VehicleId == offlineEvent.DeviceIdStorage)
                .ToListAsync());
        }

        _publisher.Reset();
        var onlineEvent = CreateEvent(
            DeviceIdTestHelper.CreateDeterministicGuid("VH-TRK-CLEAR"),
            now.AddMinutes(-1),
            55,
            4.68,
            -74.05,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        await ProcessAsync(onlineEvent);

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Empty(await db.FleetOfflinePublishMarkers
                .Where(marker => marker.VehicleId == offlineEvent.DeviceIdStorage)
                .ToListAsync());
        }

        var payload = _publisher.DeserializeVehiclePayload<VehicleLatestStatusResponse>(
            _publisher.VehicleUpdates[0].PayloadJson);
        Assert.Equal(VehicleConnectivityStatus.Online, payload?.Status);
    }

    [Fact]
    public async Task Expirador_no_duplica_evento_que_UoW_ya_publico_offline()
    {
        await ResetAsync();

        var now = new DateTimeOffset(2026, 7, 10, 17, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        var telemetryEvent = CreateEvent(DeviceIdTestHelper.CreateDeterministicGuid("VH-TRK-NODUP"), now.AddMinutes(-30), 0, 4.69, -74.04);
        await ProcessAsync(telemetryEvent);
        _publisher.Reset();

        _timeProvider.SetUtcNow(now.AddHours(2));
        var published = await PublishExpiryAsync();

        Assert.Equal(0, published);
        Assert.Empty(_publisher.VehicleUpdates);
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

    private async Task<ProcessTelemetryOutcome> ProcessAsync(TelemetryEvent telemetryEvent)
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        return await uow.ProcessAsync(telemetryEvent);
    }

    private static TelemetryEvent CreateEvent(Guid deviceId,
        DateTimeOffset timestamp,
        double speedKmh,
        double latitude,
        double longitude,
        Guid? eventId = null) =>
        TelemetryEvent.Create(
            eventId ?? Guid.NewGuid(),
            deviceId,
            "DRV-INT-001",
            timestamp,
            latitude,
            longitude,
            speedKmh,
            70,
            85);
}
