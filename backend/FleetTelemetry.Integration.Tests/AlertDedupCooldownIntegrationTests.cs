using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FleetTelemetry.Integration.Tests;

// FT-006: deduplicación, estado activo y cooldown de alertas.
public class AlertDedupCooldownIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestDatabase _database = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFleetRealtimePublisher _publisher = new();
    private IServiceProvider _services = null!;

    private static readonly DateTimeOffset T0 = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private const int CooldownSeconds = 60;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _timeProvider.SetUtcNow(T0);

        var services = new ServiceCollection();
        IntegrationTestServiceBootstrap.AddFleetTelemetryIntegrationServices(
            services,
            _database.ConnectionString,
            _timeProvider,
            configurePublisher: _publisher,
            configureAlerting: options => options.CooldownSeconds = CooldownSeconds);

        _services = services.BuildServiceProvider();
        await DatabaseInitializer.InitializeAsync(_services);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Cien_overspeed_dentro_del_cooldown_persisten_una_sola_alerta()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("OO");
        var deviceIdStorage = deviceId.ToString("D");

        for (var i = 0; i < 100; i++)
        {
            // Todos dentro de la ventana de cooldown (60s).
            _timeProvider.SetUtcNow(T0.AddMilliseconds(i));
            await ProcessAsync(CreateEvent(deviceId, speedKmh: 130));
        }

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId && a.AlertType == "overspeed"));
        var state = await db.FleetAlertStates.SingleAsync(s => s.DeviceId == deviceId && s.AlertType == "overspeed");
        Assert.True(state.IsActive);
    }

    [Fact]
    public async Task Overspeed_sostenido_despues_del_cooldown_crea_recordatorio()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("RM");
        var deviceIdStorage = deviceId.ToString("D");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 130));
        _timeProvider.SetUtcNow(T0.AddSeconds(CooldownSeconds));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 131));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(2, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId && a.AlertType == "overspeed"));
    }

    [Fact]
    public async Task Overspeed_recuperacion_y_nueva_incidencia_respetan_cooldown()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("RC");
        var deviceIdStorage = deviceId.ToString("D");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 130));
        _timeProvider.SetUtcNow(T0.AddSeconds(10));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 80));

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            var state = await db.FleetAlertStates.SingleAsync(s => s.DeviceId == deviceId && s.AlertType == "overspeed");
            Assert.False(state.IsActive);
            Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
        }

        _timeProvider.SetUtcNow(T0.AddSeconds(20));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 140));

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
            Assert.True(await db.FleetAlertStates.AnyAsync(s =>
                s.DeviceId == deviceId && s.AlertType == "overspeed" && s.IsActive));
        }

        _timeProvider.SetUtcNow(T0.AddSeconds(CooldownSeconds));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 150));

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(2, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
        }
    }

    [Fact]
    public async Task Low_fuel_y_low_battery_simultaneos_son_independientes()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("FB");
        var deviceIdStorage = deviceId.ToString("D");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 50, fuel: 10, battery: 10));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(2, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
        Assert.Equal(2, await db.FleetAlertStates.CountAsync(s => s.DeviceId == deviceId && s.IsActive));
        Assert.Contains(await db.FleetAlerts.Where(a => a.DeviceId == deviceId).Select(a => a.AlertType).ToListAsync(), t => t == "low_fuel");
        Assert.Contains(await db.FleetAlerts.Where(a => a.DeviceId == deviceId).Select(a => a.AlertType).ToListAsync(), t => t == "low_battery");
    }

    [Fact]
    public async Task Dos_vehiculos_tienen_estados_independientes()
    {
        await ResetAsync();
        var deviceA = UniqueDevice("A");
        var deviceAStorage = deviceA.ToString("D");
        var deviceB = UniqueDevice("B");
        var deviceBStorage = deviceB.ToString("D");

        await ProcessAsync(CreateEvent(deviceA, speedKmh: 130));
        await ProcessAsync(CreateEvent(deviceB, speedKmh: 130));
        await ProcessAsync(CreateEvent(deviceA, speedKmh: 135));
        await ProcessAsync(CreateEvent(deviceB, speedKmh: 135));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.FleetAlerts.CountAsync(x => x.DeviceId == deviceA));
        Assert.Equal(1, await db.FleetAlerts.CountAsync(x => x.DeviceId == deviceB));
        Assert.Equal(2, await db.FleetAlertStates.CountAsync(s => s.IsActive));
    }

    [Fact]
    public async Task Redelivery_del_mismo_EventId_no_genera_otra_alerta()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("ID");
        var deviceIdStorage = deviceId.ToString("D");
        var evt = CreateEvent(deviceId, speedKmh: 130);

        Assert.Equal(ProcessTelemetryOutcome.Processed, await ProcessAsync(evt));
        Assert.Equal(ProcessTelemetryOutcome.Duplicate, await ProcessAsync(evt));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
        Assert.Equal(1, await db.TelemetryEvents.CountAsync(e => e.EventId == evt.EventId));
    }

    [Fact]
    public async Task Reinicio_del_Worker_conserva_condicion_activa_sin_duplicar()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("WR");
        var deviceIdStorage = deviceId.ToString("D");
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 130));

        // Nuevo scope simula reinicio: el estado vive en TimescaleDB, no en memoria.
        _timeProvider.SetUtcNow(T0.AddSeconds(5));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 132));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
        Assert.True(await db.FleetAlertStates.AnyAsync(s =>
            s.DeviceId == deviceId && s.AlertType == "overspeed" && s.IsActive));
    }

    [Fact]
    public async Task Procesamiento_concurrente_mismo_VehicleId_AlertType_emite_una()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("CC");
        var deviceIdStorage = deviceId.ToString("D");
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                await using var scope = _services.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
                await uow.ProcessAsync(CreateEvent(deviceId, speedKmh: 130));
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId && a.AlertType == "overspeed"));
    }

    [Fact]
    public async Task Error_transaccional_no_deja_alerta_ni_estado_huerfanos()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("TX");
        var deviceIdStorage = deviceId.ToString("D");
        var interceptor = new FailAfterAlertEntitiesInterceptor();

        var services = new ServiceCollection();
        IntegrationTestServiceBootstrap.AddFleetTelemetryIntegrationServices(
            services,
            _database.ConnectionString,
            _timeProvider,
            configurePublisher: _publisher,
            configureAlerting: options => options.CooldownSeconds = CooldownSeconds);
        services.RemoveAll<DbContextOptions<FleetDbContext>>();
        services.RemoveAll<FleetDbContext>();
        services.AddDbContext<FleetDbContext>(options =>
            options.UseNpgsql(_database.ConnectionString).AddInterceptors(interceptor));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ProcessAsync(CreateEvent(deviceId, speedKmh: 130)));

        await using var verifyScope = _services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(0, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
        Assert.Equal(0, await db.FleetAlertStates.CountAsync(s => s.DeviceId == deviceId));
        Assert.Equal(0, await db.TelemetryEvents.CountAsync(e => e.DeviceId == deviceId));
        Assert.Equal(0, await db.ProcessedEvents.CountAsync());
    }

    private sealed class FailAfterAlertEntitiesInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            FailIfAlertPairStaged(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            FailIfAlertPairStaged(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void FailIfAlertPairStaged(DbContext? context)
        {
            if (context is null)
                return;

            var hasState = context.ChangeTracker.Entries<FleetAlertConditionStateRecord>()
                .Any(e => e.State is EntityState.Added or EntityState.Modified);
            var hasAlert = context.ChangeTracker.Entries<FleetAlertRecord>()
                .Any(e => e.State == EntityState.Added);
            if (hasState && hasAlert)
                throw new InvalidOperationException("Simulated transactional failure after alert+state staging.");
        }
    }

    [Fact]
    public async Task Alerta_reconocida_no_se_interpreta_como_recuperacion()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("AK");
        var deviceIdStorage = deviceId.ToString("D");
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 130));

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            var alert = await db.FleetAlerts.SingleAsync(a => a.DeviceId == deviceId);
            alert.IsAcknowledged = true;
            await db.SaveChangesAsync();
        }

        _timeProvider.SetUtcNow(T0.AddSeconds(5));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 135));

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
            Assert.True(await db.FleetAlerts.AnyAsync(a => a.DeviceId == deviceId && a.IsAcknowledged));
            Assert.True(await db.FleetAlertStates.AnyAsync(s =>
                s.DeviceId == deviceId && s.IsActive));
        }
    }

    [Fact]
    public async Task Publicacion_realtime_solo_alerta_insertada()
    {
        await ResetAsync();
        _publisher.Reset();
        var deviceId = UniqueDevice("RT");
        var deviceIdStorage = deviceId.ToString("D");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 130));
        Assert.Single(_publisher.AlertPayloads);

        _publisher.Reset();
        _timeProvider.SetUtcNow(T0.AddSeconds(5));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 140));
        Assert.Empty(_publisher.AlertPayloads);
    }

    [Fact]
    public async Task Fuel_null_conserva_low_fuel_activo_sin_recordatorio()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("FN");
        var deviceIdStorage = deviceId.ToString("D");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 50, fuel: 10, timestamp: T0));
        _timeProvider.SetUtcNow(T0.AddSeconds(5));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 50, fuel: null, timestamp: T0.AddSeconds(5)));

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId && a.AlertType == "low_fuel"));
            Assert.True(await db.FleetAlertStates.AnyAsync(s =>
                s.DeviceId == deviceId && s.AlertType == "low_fuel" && s.IsActive));
        }

        _publisher.Reset();
        _timeProvider.SetUtcNow(T0.AddSeconds(CooldownSeconds));
        await ProcessAsync(CreateEvent(
            deviceId, speedKmh: 50, fuel: null, timestamp: T0.AddSeconds(CooldownSeconds)));
        Assert.Empty(_publisher.AlertPayloads);

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId && a.AlertType == "low_fuel"));
            Assert.True(await db.FleetAlertStates.AnyAsync(s =>
                s.DeviceId == deviceId && s.AlertType == "low_fuel" && s.IsActive));
        }

        await ProcessAsync(CreateEvent(
            deviceId, speedKmh: 50, fuel: 50, timestamp: T0.AddSeconds(CooldownSeconds + 1)));
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.False(await db.FleetAlertStates.AnyAsync(s =>
                s.DeviceId == deviceId && s.AlertType == "low_fuel" && s.IsActive));
        }
    }

    [Fact]
    public async Task Battery_null_conserva_low_battery_activo_sin_recordatorio()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("BN");
        var deviceIdStorage = deviceId.ToString("D");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 50, battery: 10, timestamp: T0));
        _timeProvider.SetUtcNow(T0.AddSeconds(5));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 50, battery: null, timestamp: T0.AddSeconds(5)));

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId && a.AlertType == "low_battery"));
            Assert.True(await db.FleetAlertStates.AnyAsync(s =>
                s.DeviceId == deviceId && s.AlertType == "low_battery" && s.IsActive));
        }

        _publisher.Reset();
        _timeProvider.SetUtcNow(T0.AddSeconds(CooldownSeconds));
        await ProcessAsync(CreateEvent(
            deviceId, speedKmh: 50, battery: null, timestamp: T0.AddSeconds(CooldownSeconds)));
        Assert.Empty(_publisher.AlertPayloads);

        await ProcessAsync(CreateEvent(
            deviceId, speedKmh: 50, battery: 50, timestamp: T0.AddSeconds(CooldownSeconds + 1)));
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.False(await db.FleetAlertStates.AnyAsync(s =>
                s.DeviceId == deviceId && s.AlertType == "low_battery" && s.IsActive));
        }
    }

    [Fact]
    public async Task Evento_antiguo_overspeed_no_crea_alerta_ni_estado()
    {
        await ResetAsync();
        _publisher.Reset();
        var deviceId = UniqueDevice("OO");
        var deviceIdStorage = deviceId.ToString("D");
        var newer = CreateEvent(deviceId, speedKmh: 50, timestamp: T0.AddHours(2));
        var older = CreateEvent(deviceId, speedKmh: 130, timestamp: T0);

        await ProcessAsync(newer);
        _publisher.Reset();
        await ProcessAsync(older);

        Assert.Empty(_publisher.AlertPayloads);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(0, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId));
        Assert.Equal(0, await db.FleetAlertStates.CountAsync(s => s.DeviceId == deviceId));
        Assert.Equal(newer.EventId, (await db.FleetVehicleStates.SingleAsync(s => s.DeviceId == deviceId)).LastEventId);
    }

    [Fact]
    public async Task Evento_antiguo_normal_conserva_overspeed_activo()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("ON");
        var deviceIdStorage = deviceId.ToString("D");
        var newer = CreateEvent(deviceId, speedKmh: 130, timestamp: T0.AddHours(2));
        var older = CreateEvent(deviceId, speedKmh: 40, timestamp: T0);

        await ProcessAsync(newer);
        await ProcessAsync(older);

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.FleetAlerts.CountAsync(a => a.DeviceId == deviceId && a.AlertType == "overspeed"));
        Assert.True(await db.FleetAlertStates.AnyAsync(s =>
            s.DeviceId == deviceId && s.AlertType == "overspeed" && s.IsActive));
    }

    [Fact]
    public async Task Mismo_timestamp_EventId_inferior_no_modifica_condicion()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("TI");
        var deviceIdStorage = deviceId.ToString("D");
        var timestamp = T0;
        var higher = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var lower = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 130, timestamp: timestamp, eventId: higher));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 40, timestamp: timestamp, eventId: lower));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.True(await db.FleetAlertStates.AnyAsync(s =>
            s.DeviceId == deviceId && s.AlertType == "overspeed" && s.IsActive));
        Assert.Equal(higher, (await db.FleetVehicleStates.SingleAsync(s => s.DeviceId == deviceId)).LastEventId);
    }

    [Fact]
    public async Task Mismo_timestamp_EventId_superior_aplica_nueva_condicion()
    {
        await ResetAsync();
        var deviceId = UniqueDevice("TS");
        var deviceIdStorage = deviceId.ToString("D");
        var timestamp = T0;
        var lower = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var higher = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await ProcessAsync(CreateEvent(deviceId, speedKmh: 130, timestamp: timestamp, eventId: lower));
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 40, timestamp: timestamp, eventId: higher));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.False(await db.FleetAlertStates.AnyAsync(s =>
            s.DeviceId == deviceId && s.AlertType == "overspeed" && s.IsActive));
        Assert.Equal(higher, (await db.FleetVehicleStates.SingleAsync(s => s.DeviceId == deviceId)).LastEventId);
    }

    [Fact]
    public async Task Evento_antiguo_descartado_no_publica_alerta_realtime()
    {
        await ResetAsync();
        _publisher.Reset();
        var deviceId = UniqueDevice("NR");
        var deviceIdStorage = deviceId.ToString("D");
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 50, timestamp: T0.AddHours(1)));
        _publisher.Reset();
        await ProcessAsync(CreateEvent(deviceId, speedKmh: 140, timestamp: T0));

        Assert.Empty(_publisher.AlertPayloads);
        Assert.Empty(_publisher.VehicleUpdates);
    }

    private async Task<ProcessTelemetryOutcome> ProcessAsync(TelemetryEvent telemetryEvent)
    {
        await using var scope = _services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        return await uow.ProcessAsync(telemetryEvent);
    }

    private async Task ResetAsync()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        _timeProvider.SetUtcNow(T0);
        _publisher.Reset();
    }

    private static Guid UniqueDevice(string prefix) =>
        Guid.NewGuid();

    // Por defecto alinea Timestamp con TimeProvider para no descartar por fuera de orden.
    private TelemetryEvent CreateEvent(Guid deviceId,
        double speedKmh,
        double? fuel = 50,
        double? battery = 80,
        Guid? eventId = null,
        DateTimeOffset? timestamp = null) =>
        TelemetryEvent.Create(
            eventId ?? Guid.NewGuid(),
            deviceId,
            "DRV-006",
            timestamp ?? _timeProvider.GetUtcNow(),
            4.65,
            -74.08,
            speedKmh,
            fuel,
            battery);
}
