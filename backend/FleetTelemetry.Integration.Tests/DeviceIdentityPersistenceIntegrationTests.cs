using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

/// <summary>
/// Valida que DeviceId UUID es la identidad de persistencia y que vehicleName puede renombrarse
/// sin duplicar filas de estado ni perder historial/alertas.
/// Requiere Docker/Testcontainers (mismo patrón que el resto de Integration.Tests).
/// </summary>
public class DeviceIdentityPersistenceIntegrationTests : IAsyncLifetime
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
    public async Task Un_dispositivo_persiste_varios_eventos_bajo_el_mismo_DeviceId()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var deviceId = DeviceIdTestHelper.CreateDeterministicGuid("VH-ID-MULTI");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        await registry.RegisterDeviceAsync(deviceId);

        var baseTime = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 3; i++)
        {
            await uow.ProcessAsync(CreateEvent(
                deviceId,
                baseTime.AddMinutes(i),
                speedKmh: 40 + i,
                latitude: 4.60 + i * 0.01,
                longitude: -74.10));
        }

        Assert.Equal(3, await db.TelemetryEvents.CountAsync(e => e.DeviceId == deviceId));
        Assert.Single(await db.FleetVehicleStates.Where(s => s.DeviceId == deviceId).ToListAsync());
    }

    [Fact]
    public async Task Rename_mantiene_una_sola_fila_en_fleet_vehicle_state()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var deviceId = DeviceIdTestHelper.CreateDeterministicGuid("VH-ID-RENAME-STATE");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        await registry.RegisterDeviceAsync(deviceId);
        await uow.ProcessAsync(CreateEvent(
            deviceId,
            new DateTimeOffset(2026, 7, 10, 13, 0, 0, TimeSpan.Zero),
            50,
            4.65,
            -74.08));

        Assert.Equal(1, await db.FleetVehicleStates.CountAsync(s => s.DeviceId == deviceId));

        await registry.RenameDeviceAsync(deviceId, "Camion Norte");

        Assert.Equal(1, await db.FleetVehicleStates.CountAsync(s => s.DeviceId == deviceId));
        var device = await registry.GetDeviceAsync(deviceId);
        Assert.Equal("Camion Norte", device?.VehicleName);
    }

    [Fact]
    public async Task Historial_permanece_bajo_el_mismo_DeviceId_tras_rename()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var deviceId = DeviceIdTestHelper.CreateDeterministicGuid("VH-ID-RENAME-HIST");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var history = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        await registry.RegisterDeviceAsync(deviceId);
        var from = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);

        await uow.ProcessAsync(CreateEvent(deviceId, from.AddMinutes(5), 30, 4.60, -74.10));
        await uow.ProcessAsync(CreateEvent(deviceId, from.AddMinutes(15), 35, 4.61, -74.09));
        await registry.RenameDeviceAsync(deviceId, "Unidad Renombrada");

        var page = await history.GetVehicleHistoryPageAsync(deviceId, from, to, pageSize: 10, cursor: null);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(deviceId, item.DeviceId));
    }

    [Fact]
    public async Task Alertas_permanecen_bajo_el_mismo_DeviceId_tras_rename()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var deviceId = DeviceIdTestHelper.CreateDeterministicGuid("VH-ID-RENAME-ALERT");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        await registry.RegisterDeviceAsync(deviceId);
        await uow.ProcessAsync(CreateEvent(
            deviceId,
            new DateTimeOffset(2026, 7, 10, 15, 0, 0, TimeSpan.Zero),
            speedKmh: 140,
            latitude: 4.65,
            longitude: -74.08));

        Assert.True(await db.FleetAlerts.AnyAsync(a => a.DeviceId == deviceId && a.AlertType == "overspeed"));

        await registry.RenameDeviceAsync(deviceId, "Rapido 01");

        Assert.True(await db.FleetAlerts.AnyAsync(a => a.DeviceId == deviceId && a.AlertType == "overspeed"));
        Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
    }

    [Fact]
    public async Task Fleet_query_devuelve_vehicleName_actualizado_tras_rename()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var deviceId = DeviceIdTestHelper.CreateDeterministicGuid("VH-ID-QUERY-NAME");
        using var scope = _services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var registered = await registry.RegisterDeviceAsync(deviceId);
        await uow.ProcessAsync(CreateEvent(
            deviceId,
            new DateTimeOffset(2026, 7, 10, 16, 0, 0, TimeSpan.Zero),
            45,
            4.66,
            -74.07));

        var before = await fleetQuery.GetVehicleStatusAsync(deviceId);
        Assert.Equal(registered.VehicleName, before?.VehicleName);

        await registry.RenameDeviceAsync(deviceId, "Flota Centro");

        var after = await fleetQuery.GetVehicleStatusAsync(deviceId);
        Assert.Equal("Flota Centro", after?.VehicleName);
        Assert.Equal(deviceId, after?.DeviceId);
    }

    private static TelemetryEvent CreateEvent(
        Guid deviceId,
        DateTimeOffset timestamp,
        double speedKmh,
        double latitude,
        double longitude) =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            deviceId,
            "DRV-ID-001",
            timestamp,
            latitude,
            longitude,
            speedKmh,
            70,
            85);
}
