using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetTelemetry.Integration.Tests;

// FT-004: migración v3 de verificación y reparación del read model.
public class ReadModelMigrationV3IntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestDatabase _database = new();
    private readonly TestSchemaMigrationHooks _migrationHooks = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(_database.ConnectionString));
        services.AddSingleton<FleetTelemetry.Application.Interfaces.ISchemaMigrationHooks>(_migrationHooks);
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Instalacion_nueva_ejecuta_backfill_una_sola_vez()
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE TABLE fleet_vehicle_state, telemetry_events, processed_events, fleet_alerts RESTART IDENTITY CASCADE;
            DELETE FROM schema_versions WHERE "Version" IN (2, 3);
            """;
        await command.ExecuteNonQueryAsync();

        await SeedTelemetryOnlyAsync(
            ("VH-NEW-001", new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero), Guid.Parse("abababab-abab-abab-abab-abababababab"), 30, 4.60, -74.10));

        _migrationHooks.Reset();
        await DatabaseInitializer.InitializeAsync(_services);

        Assert.True(await SchemaVersionExistsAsync(2));
        Assert.True(await SchemaVersionExistsAsync(3));
        Assert.Equal(1, _migrationHooks.BackfillCount);
    }

    [Fact]
    public async Task V2_heredada_ejecuta_reparacion_v3()
    {
        await ResetForV3RepairAsync(includeV2: true, includeV3: false);
        await SeedTelemetryOnlyAsync(
            ("VH-V3-001", new DateTimeOffset(2026, 7, 10, 8, 30, 0, TimeSpan.Zero), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 50, 4.65, -74.08));

        _migrationHooks.Reset();
        await DatabaseInitializer.InitializeAsync(_services);

        Assert.True(await SchemaVersionExistsAsync(3));

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == "VH-V3-001");
        Assert.Equal(50, state.SpeedKmh);
        Assert.Equal(1, _migrationHooks.BackfillCount);
    }

    [Fact]
    public async Task V2_presente_y_estado_parcial_es_completado()
    {
        await ResetForV3RepairAsync(includeV2: true, includeV3: false);
        await SeedTelemetryOnlyAsync(
            ("VH-V3-PARTIAL", new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 20, 4.55, -74.12),
            ("VH-V3-MISSING", new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero), Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 40, 4.63, -74.07));

        await SeedPartialFleetStateAsync(
            ("VH-V3-PARTIAL", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero), 20, 4.55, -74.12));

        _migrationHooks.Reset();
        await DatabaseInitializer.InitializeAsync(_services);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(2, await db.FleetVehicleStates.CountAsync());
    }

    [Fact]
    public async Task V3_no_retrocede_estado_mas_reciente()
    {
        await ResetForV3RepairAsync(includeV2: true, includeV3: false);
        var newerTimestamp = new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero);
        var olderTimestamp = new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

        await SeedTelemetryOnlyAsync(
            ("VH-V3-NO-REG", newerTimestamp, Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 70, 4.71, -74.04),
            ("VH-V3-NO-REG", olderTimestamp, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), 15, 4.50, -74.15));

        await SeedPartialFleetStateAsync(
            ("VH-V3-NO-REG", Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), newerTimestamp, 70, 4.71, -74.04));

        await DatabaseInitializer.InitializeAsync(_services);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var state = await db.FleetVehicleStates.SingleAsync(s => s.VehicleId == "VH-V3-NO-REG");
        Assert.Equal(newerTimestamp, state.LastTimestamp);
        Assert.Equal(70, state.SpeedKmh);
    }

    [Fact]
    public async Task Fallo_v3_no_registra_version_3()
    {
        await ResetForV3RepairAsync(includeV2: true, includeV3: false);
        await SeedTelemetryOnlyAsync(
            ("VH-V3-FAIL", new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero), Guid.Parse("10101010-1010-1010-1010-101010101010"), 25, 4.58, -74.11));

        _migrationHooks.ThrowOnVersionRegister = true;
        _migrationHooks.ThrowOnVersion = 3;

        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(_services));

        Assert.False(await SchemaVersionExistsAsync(3));
    }

    [Fact]
    public async Task Segunda_ejecucion_no_repite_v3()
    {
        await ResetForV3RepairAsync(includeV2: true, includeV3: false);
        await SeedTelemetryOnlyAsync(
            ("VH-V3-ONCE", new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero), Guid.Parse("12121212-1212-1212-1212-121212121212"), 40, 4.63, -74.07));

        _migrationHooks.Reset();
        await DatabaseInitializer.InitializeAsync(_services);
        var countAfterFirst = _migrationHooks.BackfillCount;

        await DatabaseInitializer.InitializeAsync(_services);

        Assert.Equal(1, countAfterFirst);
        Assert.Equal(1, _migrationHooks.BackfillCount);
        Assert.True(await SchemaVersionExistsAsync(3));
    }

    private async Task ResetForV3RepairAsync(bool includeV2, bool includeV3)
    {
        _migrationHooks.ThrowOnVersionRegister = false;
        _migrationHooks.Reset();

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE TABLE fleet_vehicle_state RESTART IDENTITY CASCADE;
            DELETE FROM schema_versions WHERE "Version" IN (2, 3);
            TRUNCATE TABLE telemetry_events RESTART IDENTITY CASCADE;
            """;
        await command.ExecuteNonQueryAsync();

        if (includeV2)
        {
            await using var v2 = connection.CreateCommand();
            v2.CommandText = """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (2, NOW(), 'legacy v2 record');
                """;
            await v2.ExecuteNonQueryAsync();
        }

        if (includeV3)
        {
            await using var v3 = connection.CreateCommand();
            v3.CommandText = """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (3, NOW(), 'legacy v3 record');
                """;
            await v3.ExecuteNonQueryAsync();
        }
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

    private async Task SeedPartialFleetStateAsync(
        params (string VehicleId, Guid EventId, DateTimeOffset Timestamp, double SpeedKmh, double Lat, double Lng)[] states)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        foreach (var item in states)
        {
            db.FleetVehicleStates.Add(new FleetVehicleStateRecord
            {
                VehicleId = item.VehicleId,
                LastEventId = item.EventId,
                LastTimestamp = item.Timestamp,
                Latitude = item.Lat,
                Longitude = item.Lng,
                SpeedKmh = item.SpeedKmh,
                LocationSource = "gps",
                UpdatedAt = item.Timestamp,
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
}
