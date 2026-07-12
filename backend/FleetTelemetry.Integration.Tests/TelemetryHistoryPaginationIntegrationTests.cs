using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

// FT-004 tests 20-28: historial keyset, to estable y validación de rangos.
public class TelemetryHistoryPaginationIntegrationTests : IAsyncLifetime
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
    public async Task Primera_pagina_historial_orden_descendente_por_timestamp()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var vehicleId = "VH-HIST-001";
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        await SeedHistoryEventsAsync(
            (vehicleId, baseTime.AddMinutes(10), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            (vehicleId, baseTime.AddMinutes(20), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            (vehicleId, baseTime.AddMinutes(30), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var page = await repository.GetVehicleHistoryPageAsync(
            vehicleId,
            baseTime,
            baseTime.AddHours(1),
            pageSize: 2,
            cursor: null);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(baseTime.AddMinutes(30), page.Items[0].Timestamp);
        Assert.Equal(baseTime.AddMinutes(20), page.Items[1].Timestamp);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Segunda_pagina_historial_usa_keyset_cursor()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var vehicleId = "VH-HIST-002";
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        await SeedHistoryEventsAsync(
            (vehicleId, baseTime.AddMinutes(10), Guid.Parse("11111111-1111-1111-1111-111111111111")),
            (vehicleId, baseTime.AddMinutes(20), Guid.Parse("22222222-2222-2222-2222-222222222222")),
            (vehicleId, baseTime.AddMinutes(30), Guid.Parse("33333333-3333-3333-3333-333333333333")));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var from = baseTime;
        var to = baseTime.AddHours(1);
        var first = await repository.GetVehicleHistoryPageAsync(vehicleId, from, to, pageSize: 1, cursor: null);
        var second = await repository.GetVehicleHistoryPageAsync(vehicleId, from, to, pageSize: 1, cursor: first.NextCursor);

        Assert.Single(second.Items);
        Assert.Equal(baseTime.AddMinutes(20), second.Items[0].Timestamp);
        Assert.DoesNotContain(second.Items, e => e.EventId == first.Items[0].EventId);
    }

    [Fact]
    public async Task Paginacion_historial_recorre_todos_los_eventos_sin_duplicados()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var vehicleId = "VH-HIST-003";
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        var eventIds = Enumerable.Range(1, 7)
            .Select(i => Guid.Parse($"dddddddd-dddd-dddd-dddd-{i:D12}"))
            .ToArray();

        var seed = eventIds
            .Select((id, index) => (vehicleId, baseTime.AddMinutes(index * 5), id))
            .ToArray();
        await SeedHistoryEventsAsync(seed);

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var from = baseTime;
        var to = baseTime.AddHours(2);
        var collected = new List<Guid>();
        string? cursor = null;

        while (true)
        {
            var page = await repository.GetVehicleHistoryPageAsync(vehicleId, from, to, pageSize: 3, cursor);
            collected.AddRange(page.Items.Select(e => e.EventId));

            if (!page.HasMore || string.IsNullOrWhiteSpace(page.NextCursor))
                break;

            cursor = page.NextCursor;
        }

        Assert.Equal(eventIds.Length, collected.Count);
        Assert.Equal(eventIds.Reverse(), collected);
        Assert.Equal(collected.Distinct().Count(), collected.Count);
    }

    [Fact]
    public async Task Cursor_estable_con_mismo_to_excluye_eventos_posteriores_al_limite()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var vehicleId = "VH-HIST-004";
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        var to = baseTime.AddMinutes(25);

        await SeedHistoryEventsAsync(
            (vehicleId, baseTime.AddMinutes(10), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            (vehicleId, baseTime.AddMinutes(20), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            (vehicleId, baseTime.AddMinutes(30), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var from = baseTime;
        var first = await repository.GetVehicleHistoryPageAsync(vehicleId, from, to, pageSize: 1, cursor: null);

        await SeedHistoryEventsAsync(
            (vehicleId, baseTime.AddMinutes(40), Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")));

        var second = await repository.GetVehicleHistoryPageAsync(vehicleId, from, to, pageSize: 10, cursor: first.NextCursor);
        var fullWithinBound = await repository.GetVehicleHistoryPageAsync(vehicleId, from, to, pageSize: 10, cursor: null);

        Assert.Equal(baseTime.AddMinutes(20), first.Items[0].Timestamp);
        Assert.Single(second.Items);
        Assert.Equal(baseTime.AddMinutes(10), second.Items[0].Timestamp);
        Assert.Equal(2, fullWithinBound.Items.Count);
        Assert.DoesNotContain(fullWithinBound.Items, e => e.Timestamp > to);
    }

    [Fact]
    public async Task from_igual_o_posterior_a_to_lanza_ArgumentOutOfRangeException()
    {
        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var timestamp = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.GetVehicleHistoryPageAsync("VH-001", timestamp, timestamp, pageSize: 10, cursor: null));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.GetVehicleHistoryPageAsync("VH-001", timestamp.AddHours(1), timestamp, pageSize: 10, cursor: null));
    }

    [Fact]
    public async Task Rango_historial_supera_maximo_dias_lanza_ArgumentOutOfRangeException()
    {
        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(8);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.GetVehicleHistoryPageAsync("VH-001", from, to, pageSize: 10, cursor: null));
    }

    [Fact]
    public async Task pageSize_historial_fuera_de_rango_lanza_ArgumentOutOfRangeException()
    {
        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var from = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.GetVehicleHistoryPageAsync("VH-001", from, to, pageSize: 0, cursor: null));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.GetVehicleHistoryPageAsync("VH-001", from, to, pageSize: 1001, cursor: null));
    }

    [Fact]
    public async Task Cursor_de_otro_vehiculo_lanza_InvalidCursorException()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        await SeedHistoryEventsAsync(
            ("VH-A", baseTime.AddMinutes(20), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            ("VH-A", baseTime.AddMinutes(10), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            ("VH-B", baseTime.AddMinutes(10), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var from = baseTime;
        var to = baseTime.AddHours(1);
        var page = await repository.GetVehicleHistoryPageAsync("VH-A", from, to, pageSize: 1, cursor: null);

        Assert.NotNull(page.NextCursor);

        await Assert.ThrowsAsync<InvalidCursorException>(() =>
            repository.GetVehicleHistoryPageAsync("VH-B", from, to, pageSize: 1, cursor: page.NextCursor));
    }

    [Fact]
    public async Task Cursor_con_rango_distinto_lanza_InvalidCursorException()
    {
        await IntegrationTestServiceBootstrap.ResetFleetDataAsync(_services);

        var vehicleId = "VH-HIST-005";
        var baseTime = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        await SeedHistoryEventsAsync(
            (vehicleId, baseTime.AddMinutes(10), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            (vehicleId, baseTime.AddMinutes(20), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")));

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var from = baseTime;
        var to = baseTime.AddHours(1);
        var page = await repository.GetVehicleHistoryPageAsync(vehicleId, from, to, pageSize: 1, cursor: null);

        await Assert.ThrowsAsync<InvalidCursorException>(() =>
            repository.GetVehicleHistoryPageAsync(vehicleId, from, to.AddMinutes(30), pageSize: 1, cursor: page.NextCursor));

        await Assert.ThrowsAsync<InvalidCursorException>(() =>
            repository.GetVehicleHistoryPageAsync(vehicleId, from.AddMinutes(5), to, pageSize: 1, cursor: page.NextCursor));
    }

    private async Task SeedHistoryEventsAsync(params (string VehicleId, DateTimeOffset Timestamp, Guid EventId)[] events)
    {
        using var scope = _services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITelemetryProcessingUnitOfWork>();

        foreach (var item in events)
        {
            var telemetryEvent = TelemetryEvent.Create(
                item.EventId,
                item.VehicleId,
                "DRV-HIST",
                item.Timestamp,
                4.65,
                -74.08,
                40,
                70,
                85);

            await uow.ProcessAsync(telemetryEvent);
        }
    }
}
