using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

// FT-004 tests 9-19: paginación por cursor, filtros y flota de 1500 vehículos.
public class FleetPaginationIntegrationTests : IAsyncLifetime
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
    public async Task Primera_pagina_respeta_pageSize()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);
        await SeedFleetStatesAsync(5, now.AddMinutes(-1));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var page = await fleetQuery.GetFleetPageAsync(pageSize: 2, cursor: null);

        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(page.NextCursor));
    }

    [Fact]
    public async Task Segunda_pagina_usa_cursor_sin_duplicados()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);
        await SeedFleetStatesAsync(5, now.AddMinutes(-1));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var first = await fleetQuery.GetFleetPageAsync(pageSize: 2, cursor: null);
        var second = await fleetQuery.GetFleetPageAsync(pageSize: 2, cursor: first.NextCursor);

        Assert.Equal(2, second.Items.Count);
        Assert.DoesNotContain(second.Items, item => first.Items.Any(f => f.VehicleId == item.VehicleId));
    }

    [Fact]
    public async Task Paginacion_completa_recorre_toda_la_flota_sin_perdidas()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);
        const int total = 17;
        await SeedFleetStatesAsync(total, now.AddMinutes(-1));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var collected = new List<string>();
        string? cursor = null;

        while (true)
        {
            var page = await fleetQuery.GetFleetPageAsync(pageSize: 5, cursor);
            collected.AddRange(page.Items.Select(i => i.VehicleId));

            if (!page.HasMore || string.IsNullOrWhiteSpace(page.NextCursor))
                break;

            cursor = page.NextCursor;
        }

        Assert.Equal(total, collected.Count);
        Assert.Equal(collected.OrderBy(id => id, StringComparer.Ordinal), collected);
        Assert.Equal(collected.Distinct().Count(), collected.Count);
    }

    [Fact]
    public async Task liveOnly_filtra_vehiculos_dentro_de_ventana_online()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedFleetStatesWithTimestampsAsync(
            ("VH-ONLINE-1", now.AddMinutes(-2)),
            ("VH-ONLINE-2", now.AddMinutes(-4)),
            ("VH-OFFLINE-1", now.AddMinutes(-10)),
            ("VH-OFFLINE-2", now.AddMinutes(-30)));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var livePage = await fleetQuery.GetFleetPageAsync(pageSize: 10, cursor: null, liveOnly: true);
        var allPage = await fleetQuery.GetFleetPageAsync(pageSize: 10, cursor: null, liveOnly: false);

        Assert.Equal(2, livePage.Items.Count);
        Assert.All(livePage.Items, item => Assert.Equal("online", item.Status));
        Assert.Equal(4, allPage.Items.Count);
    }

    [Fact]
    public async Task excludeSimulated_omite_vehiculos_simulados()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedFleetStatesWithSourcesAsync(
            ("VH-GPS-1", "gps", now.AddMinutes(-1)),
            ("VH-GPS-2", "gps", now.AddMinutes(-1)),
            ("VH-SIM-1", "simulated", now.AddMinutes(-1)));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var filtered = await fleetQuery.GetFleetPageAsync(
            pageSize: 10,
            cursor: null,
            excludeSimulated: true);
        var all = await fleetQuery.GetFleetPageAsync(
            pageSize: 10,
            cursor: null,
            excludeSimulated: false);

        Assert.Equal(2, filtered.Items.Count);
        Assert.DoesNotContain(filtered.Items, item => item.LastLocationSource == "simulated");
        Assert.Equal(3, all.Items.Count);
    }

    [Fact]
    public async Task Cursor_con_filtros_distintos_lanza_InvalidCursorException()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);
        await SeedFleetStatesAsync(3, now.AddMinutes(-1));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var page = await fleetQuery.GetFleetPageAsync(pageSize: 1, cursor: null, liveOnly: false, excludeSimulated: false);

        await Assert.ThrowsAsync<InvalidCursorException>(() =>
            fleetQuery.GetFleetPageAsync(pageSize: 1, cursor: page.NextCursor, liveOnly: true, excludeSimulated: false));

        await Assert.ThrowsAsync<InvalidCursorException>(() =>
            fleetQuery.GetFleetPageAsync(pageSize: 1, cursor: page.NextCursor, liveOnly: false, excludeSimulated: true));
    }

    [Fact]
    public async Task pageSize_fuera_de_rango_lanza_ArgumentOutOfRangeException()
    {
        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fleetQuery.GetFleetPageAsync(pageSize: 0, cursor: null));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fleetQuery.GetFleetPageAsync(pageSize: 501, cursor: null));
    }

    [Fact]
    public async Task GetAllFleetStatusesAsync_recolecta_todas_las_paginas()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);
        const int total = 12;
        await SeedFleetStatesAsync(total, now.AddMinutes(-1));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var all = await fleetQuery.GetAllFleetStatusesAsync();

        Assert.Equal(total, all.Count);
        Assert.Equal(all.OrderBy(v => v.VehicleId, StringComparer.Ordinal).Select(v => v.VehicleId), all.Select(v => v.VehicleId));
    }

    [Fact]
    public async Task Orden_deterministico_por_VehicleId_ascendente()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        await SeedFleetStatesWithTimestampsAsync(
            ("VH-ZZZ", now.AddMinutes(-1)),
            ("VH-AAA", now.AddMinutes(-1)),
            ("VH-MMM", now.AddMinutes(-1)));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var page = await fleetQuery.GetFleetPageAsync(pageSize: 10, cursor: null);

        Assert.Equal(["VH-AAA", "VH-MMM", "VH-ZZZ"], page.Items.Select(i => i.VehicleId));
    }

    [Fact]
    public async Task Flota_de_1500_vehiculos_pagina_sin_duplicados_ni_saltos()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);
        const int total = 1500;
        await SeedFleetStatesAsync(total, now.AddMinutes(-1));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(total, await db.FleetVehicleStates.CountAsync());

        var collected = new List<string>();
        string? cursor = null;

        while (true)
        {
            var page = await fleetQuery.GetFleetPageAsync(pageSize: 200, cursor);
            collected.AddRange(page.Items.Select(i => i.VehicleId));

            if (!page.HasMore || string.IsNullOrWhiteSpace(page.NextCursor))
                break;

            cursor = page.NextCursor;
        }

        Assert.Equal(total, collected.Count);
        Assert.Equal(total, collected.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(1, total).Select(i => $"VH-{i:D4}"), collected);
    }

    [Fact]
    public async Task Vehiculo_con_GPS_antiguo_y_simulated_reciente_es_excluido()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);

        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        var vehicleId = "VH-MIX-001";
        await uow.ProcessAsync(TelemetryEvent.Create(
            Guid.NewGuid(), vehicleId, null, now.AddMinutes(-30), 4.6, -74.0, 40, 70, 90, "gps"));
        await uow.ProcessAsync(TelemetryEvent.Create(
            Guid.NewGuid(), vehicleId, null, now.AddMinutes(-1), 4.61, -74.01, 0, 70, 90, "simulated"));

        var filtered = await fleetQuery.GetFleetPageAsync(pageSize: 10, cursor: null, excludeSimulated: true);
        var all = await fleetQuery.GetFleetPageAsync(pageSize: 10, cursor: null, excludeSimulated: false);

        Assert.DoesNotContain(filtered.Items, item => item.VehicleId == vehicleId);
        var stored = Assert.Single(all.Items, item => item.VehicleId == vehicleId);
        Assert.Equal("simulated", stored.LastLocationSource);
    }

    [Fact]
    public async Task Ultima_pagina_tiene_hasMore_false_y_nextCursor_null()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        _timeProvider.SetUtcNow(now);
        await SeedFleetStatesAsync(3, now.AddMinutes(-1));

        using var scope = _services.CreateScope();
        var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();

        string? cursor = null;
        CursorPage<VehicleLatestStatusResponse>? lastPage = null;

        while (true)
        {
            var page = await fleetQuery.GetFleetPageAsync(pageSize: 2, cursor);
            lastPage = page;

            if (!page.HasMore || string.IsNullOrWhiteSpace(page.NextCursor))
                break;

            cursor = page.NextCursor;
        }

        Assert.NotNull(lastPage);
        Assert.False(lastPage.HasMore);
        Assert.Null(lastPage.NextCursor);
        Assert.Single(lastPage.Items);
    }

    private async Task SeedFleetStatesAsync(int count, DateTimeOffset lastTimestamp)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        var states = Enumerable.Range(1, count)
            .Select(i => new FleetVehicleStateRecord
            {
                VehicleId = $"VH-{i:D4}",
                LastEventId = Guid.NewGuid(),
                LastTimestamp = lastTimestamp,
                Latitude = 4.60 + i * 0.0001,
                Longitude = -74.10,
                SpeedKmh = 30,
                LocationSource = "gps",
                UpdatedAt = lastTimestamp,
            })
            .ToList();

        const int batchSize = 500;
        for (var offset = 0; offset < states.Count; offset += batchSize)
        {
            var batch = states.Skip(offset).Take(batchSize).ToList();
            db.FleetVehicleStates.AddRange(batch);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    private async Task SeedFleetStatesWithTimestampsAsync(params (string VehicleId, DateTimeOffset LastTimestamp)[] states)
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
                SpeedKmh = 25,
                LocationSource = "gps",
                UpdatedAt = item.LastTimestamp,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedFleetStatesWithSourcesAsync(
        params (string VehicleId, string LocationSource, DateTimeOffset LastTimestamp)[] states)
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
                SpeedKmh = 25,
                LocationSource = item.LocationSource,
                UpdatedAt = item.LastTimestamp,
            });
        }

        await db.SaveChangesAsync();
    }
}
