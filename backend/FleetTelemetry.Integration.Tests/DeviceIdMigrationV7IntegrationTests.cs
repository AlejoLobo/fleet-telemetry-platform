using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetTelemetry.Integration.Tests;

/// <summary>
/// Valida migración v7: reutiliza DeviceId registrado por vehicle_name,
/// sincroniza secuencia VH-### y no falla al reentrar tras schema device_id.
/// </summary>
public class DeviceIdMigrationV7IntegrationTests : IAsyncLifetime
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
        services.AddSingleton<ISchemaMigrationHooks>(_migrationHooks);
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Migration_v7_reuses_registered_device_id_for_matching_vehicle_name()
    {
        _migrationHooks.ThrowOnVersionRegister = true;
        _migrationHooks.ThrowOnVersion = 7;

        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(_services));

        _migrationHooks.ThrowOnVersionRegister = false;

        var deviceIdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        Assert.True(await SchemaVersionExistsAsync(connection, 6));
        Assert.False(await SchemaVersionExistsAsync(connection, 7));
        Assert.True(await ColumnExistsAsync(connection, "telemetry_events", "VehicleId"));

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO fleet_devices (device_id, vehicle_name, created_at, updated_at)
                VALUES (@deviceId, 'VH-001', NOW(), NOW())
                ON CONFLICT (device_id) DO NOTHING;

                INSERT INTO telemetry_events (
                    "EventId", "VehicleId", "DriverId", "Timestamp",
                    "Latitude", "Longitude", "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "CapturedAt")
                VALUES (
                    @eventId, 'VH-001', 'DRV-1', @ts,
                    4.65, -74.08, 40, 50, 80, NOW());
                """;
            seed.Parameters.AddWithValue("deviceId", deviceIdA);
            seed.Parameters.AddWithValue("eventId", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
            seed.Parameters.AddWithValue("ts", new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
            await seed.ExecuteNonQueryAsync();
        }

        await DatabaseInitializer.InitializeAsync(_services);

        Assert.True(await SchemaVersionExistsAsync(connection, 7));
        Assert.True(await ColumnExistsAsync(connection, "telemetry_events", "device_id"));

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT device_id
                FROM telemetry_events
                WHERE "EventId" = @eventId;
                """;
            query.Parameters.AddWithValue("eventId", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
            var mapped = (Guid)(await query.ExecuteScalarAsync() ?? Guid.Empty);
            Assert.Equal(deviceIdA, mapped);
        }

        await using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM fleet_devices WHERE vehicle_name = 'VH-001';";
            Assert.Equal(1, Convert.ToInt32(await count.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task Sequence_sync_after_seeded_vh_names_allocates_next()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO fleet_devices (device_id, vehicle_name, created_at, updated_at)
                SELECT
                    gen_random_uuid(),
                    'VH-' || lpad(g::text, 3, '0'),
                    NOW(),
                    NOW()
                FROM generate_series(1, 100) AS g;
                """;
            await seed.ExecuteNonQueryAsync();
        }

        await SyncSequenceWithProductionLogicAsync(connection);

        var registered = await RegisterWithFreshScopeAsync(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        Assert.Equal("VH-101", registered.VehicleName);
    }

    [Fact]
    public async Task Empty_vh_sequence_next_registration_is_vh001()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using (var truncate = connection.CreateCommand())
        {
            truncate.CommandText = "TRUNCATE TABLE fleet_devices;";
            await truncate.ExecuteNonQueryAsync();
        }

        await SyncSequenceWithProductionLogicAsync(connection);

        var registered = await RegisterWithFreshScopeAsync(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        Assert.Equal("VH-001", registered.VehicleName);
    }

    [Fact]
    public async Task Free_text_names_without_vh_prefix_yield_vh001()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                TRUNCATE TABLE fleet_devices;
                INSERT INTO fleet_devices (device_id, vehicle_name, created_at, updated_at)
                VALUES
                    (gen_random_uuid(), 'Camión Norte', NOW(), NOW()),
                    (gen_random_uuid(), 'Unidad Sur', NOW(), NOW());
                """;
            await seed.ExecuteNonQueryAsync();
        }

        await SyncSequenceWithProductionLogicAsync(connection);

        var registered = await RegisterWithFreshScopeAsync(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        Assert.Equal("VH-001", registered.VehicleName);
    }

    [Fact]
    public async Task Sequence_sync_reentry_preserves_next_allocation()
    {
        await DatabaseInitializer.InitializeAsync(_services);
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await SyncSequenceWithProductionLogicAsync(connection);

        var first = await RegisterWithFreshScopeAsync(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Assert.Equal("VH-001", first.VehicleName);

        await SyncSequenceWithProductionLogicAsync(connection);
        var second = await RegisterWithFreshScopeAsync(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.Equal("VH-002", second.VehicleName);
    }

    private static async Task SyncSequenceWithProductionLogicAsync(NpgsqlConnection connection)
    {
        await using var sync = connection.CreateCommand();
        sync.CommandText = """
            WITH current_max AS (
                SELECT MAX(
                    substring(vehicle_name FROM '^VH-([0-9]+)$')::bigint
                ) AS value
                FROM fleet_devices
                WHERE vehicle_name ~ '^VH-[0-9]+$'
            )
            SELECT setval(
                'fleet_vehicle_name_seq',
                COALESCE(value, 1),
                value IS NOT NULL
            )
            FROM current_max;
            """;
        await sync.ExecuteNonQueryAsync();
    }

    private async Task<FleetTelemetry.Domain.Entities.FleetDevice> RegisterWithFreshScopeAsync(Guid deviceId)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(_database.ConnectionString));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IDeviceRegistry, FleetTelemetry.Infrastructure.Repositories.TimescaleDeviceRegistry>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistry>();
        return await registry.RegisterDeviceAsync(deviceId);
    }

    private static async Task<bool> SchemaVersionExistsAsync(NpgsqlConnection connection, int version)
    {
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

    private static async Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection,
        string tableName,
        string columnName)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @tableName
                  AND column_name = @columnName
            );
            """,
            connection);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("columnName", columnName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }
}
