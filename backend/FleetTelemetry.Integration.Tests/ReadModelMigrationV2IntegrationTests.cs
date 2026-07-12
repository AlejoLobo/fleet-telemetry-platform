using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetTelemetry.Integration.Tests;

// FT-004: migración v2 atómica y backfill ejecutado una sola vez.
public class ReadModelMigrationV2IntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestDatabase _database = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(_database.ConnectionString));
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        DatabaseInitializer.SimulateBackfillFailureForTests = false;
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Primera_ejecucion_aplica_v2_y_backfill()
    {
        await ResetSchemaForV2TestAsync();
        await SeedTelemetryOnlyAsync(
            ("VH-MV2-001", new DateTimeOffset(2026, 7, 10, 8, 10, 0, TimeSpan.Zero), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 30, 4.60, -74.10),
            ("VH-MV2-001", new DateTimeOffset(2026, 7, 10, 8, 30, 0, TimeSpan.Zero), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 50, 4.65, -74.08));

        DatabaseInitializer.ResetBackfillExecutionCountForTests();
        await DatabaseInitializer.InitializeAsync(_services);

        Assert.Equal(1, DatabaseInitializer.BackfillExecutionCount);
        Assert.True(await SchemaVersionExistsAsync(2));

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == "VH-MV2-001");
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), state.LastEventId);
    }

    [Fact]
    public async Task Segunda_ejecucion_no_repite_backfill()
    {
        await ResetSchemaForV2TestAsync();
        await SeedTelemetryOnlyAsync(
            ("VH-MV2-002", new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 20, 4.55, -74.12));

        DatabaseInitializer.ResetBackfillExecutionCountForTests();
        await DatabaseInitializer.InitializeAsync(_services);
        var countAfterFirst = DatabaseInitializer.BackfillExecutionCount;

        await DatabaseInitializer.InitializeAsync(_services);

        Assert.Equal(1, countAfterFirst);
        Assert.Equal(1, DatabaseInitializer.BackfillExecutionCount);
    }

    [Fact]
    public async Task Fallo_de_backfill_no_registra_version_2()
    {
        await ResetSchemaForV2TestAsync();
        await SeedTelemetryOnlyAsync(
            ("VH-MV2-FAIL", new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero), Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 25, 4.58, -74.11));

        DatabaseInitializer.SimulateBackfillFailureForTests = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(_services));

        Assert.False(await SchemaVersionExistsAsync(2));
    }

    [Fact]
    public async Task Fallo_de_backfill_revierte_fleet_vehicle_state()
    {
        await ResetSchemaForV2TestAsync();
        await SeedTelemetryOnlyAsync(
            ("VH-MV2-ROLL", new DateTimeOffset(2026, 7, 10, 10, 30, 0, TimeSpan.Zero), Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 35, 4.62, -74.09));

        DatabaseInitializer.SimulateBackfillFailureForTests = true;

        try
        {
            await DatabaseInitializer.InitializeAsync(_services);
        }
        catch (InvalidOperationException)
        {
            // esperado
        }

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(0, await db.FleetVehicleStates.CountAsync());
    }

    [Fact]
    public async Task Dos_inicializadores_concurrentes_aplican_v2_una_vez()
    {
        await ResetSchemaForV2TestAsync();
        await SeedTelemetryOnlyAsync(
            ("VH-MV2-CONC-001", new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero), Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), 40, 4.63, -74.07),
            ("VH-MV2-CONC-002", new DateTimeOffset(2026, 7, 10, 11, 5, 0, TimeSpan.Zero), Guid.Parse("10101010-1010-1010-1010-101010101010"), 45, 4.64, -74.06));

        DatabaseInitializer.ResetBackfillExecutionCountForTests();

        var tasks = Enumerable.Range(0, 4)
            .Select(_ => DatabaseInitializer.InitializeAsync(_services))
            .ToArray();

        var exceptions = await Task.WhenAll(tasks.Select(CaptureExceptionAsync));
        Assert.All(exceptions, Assert.Null);
        Assert.Equal(1, DatabaseInitializer.BackfillExecutionCount);
        Assert.True(await SchemaVersionExistsAsync(2));

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(2, await db.FleetVehicleStates.CountAsync());
    }

    [Fact]
    public async Task Backfill_preserva_estado_mas_reciente()
    {
        await ResetSchemaForV2TestAsync();
        var baseTime = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        await SeedTelemetryOnlyAsync(
            ("VH-MV2-BF", baseTime.AddMinutes(10), Guid.Parse("12121212-1212-1212-1212-121212121212"), 30, 4.60, -74.10),
            ("VH-MV2-BF", baseTime.AddMinutes(30), Guid.Parse("13131313-1313-1313-1313-131313131313"), 55, 4.66, -74.08));

        await DatabaseInitializer.InitializeAsync(_services);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == "VH-MV2-BF");
        Assert.Equal(Guid.Parse("13131313-1313-1313-1313-131313131313"), state.LastEventId);
        Assert.Equal(55, state.SpeedKmh);
    }

    [Fact]
    public async Task Version_2_se_registra_despues_del_backfill()
    {
        await ResetSchemaForV2TestAsync();
        await SeedTelemetryOnlyAsync(
            ("VH-MV2-ORDER", new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), Guid.Parse("14141414-1414-1414-1414-141414141414"), 60, 4.68, -74.05));

        await DatabaseInitializer.InitializeAsync(_services);

        Assert.True(await SchemaVersionExistsAsync(2));

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.FleetVehicleStates.CountAsync());
    }

    private async Task ResetSchemaForV2TestAsync()
    {
        DatabaseInitializer.SimulateBackfillFailureForTests = false;

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE TABLE fleet_vehicle_state RESTART IDENTITY CASCADE;
            DELETE FROM schema_versions WHERE "Version" = 2;
            TRUNCATE TABLE telemetry_events RESTART IDENTITY CASCADE;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedTelemetryOnlyAsync(
        params (string VehicleId, DateTimeOffset Timestamp, Guid EventId, double SpeedKmh, double Lat, double Lng)[] events)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        foreach (var item in events)
        {
            db.TelemetryEvents.Add(new TelemetryEventRecord
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

    private async Task<bool> SchemaVersionExistsAsync(int version)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM schema_versions WHERE "Version" = @version
            );
            """,
            connection);
        command.Parameters.AddWithValue("version", version);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
