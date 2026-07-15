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
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(_database.ConnectionString));
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Migration_v7_reuses_registered_device_id_for_matching_vehicle_name()
    {
        var isolated = new IntegrationTestDatabase();
        try
        {
            await isolated.InitializeEmptyAsync();
            // CI usa DB compartida: hay que vaciar public para poder interrumpir v7.
            await isolated.ResetPublicSchemaAsync();

            var hooks = new TestSchemaMigrationHooks
            {
                ThrowOnVersionRegister = true,
                ThrowOnVersion = 7,
            };
            await using var services = BuildServices(isolated.ConnectionString, hooks);

            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(services));

            hooks.ThrowOnVersionRegister = false;

            var deviceIdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            await using var connection = new NpgsqlConnection(isolated.ConnectionString);
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

            await DatabaseInitializer.InitializeAsync(services);

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
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sequence_sync_after_seeded_vh_names_allocates_next()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using (var truncate = connection.CreateCommand())
        {
            truncate.CommandText = "TRUNCATE TABLE fleet_devices;";
            await truncate.ExecuteNonQueryAsync();
        }

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

        await using (var truncate = connection.CreateCommand())
        {
            truncate.CommandText = "TRUNCATE TABLE fleet_devices;";
            await truncate.ExecuteNonQueryAsync();
        }

        await SyncSequenceWithProductionLogicAsync(connection);

        var first = await RegisterWithFreshScopeAsync(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Assert.Equal("VH-001", first.VehicleName);

        await SyncSequenceWithProductionLogicAsync(connection);
        var second = await RegisterWithFreshScopeAsync(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.Equal("VH-002", second.VehicleName);
    }

    [Fact]
    public async Task Orphan_telemetry_across_two_chunks_gets_stable_event_keys()
    {
        var isolated = new IntegrationTestDatabase();
        try
        {
            await isolated.InitializeEmptyAsync();

            var hooks = new TestSchemaMigrationHooks
            {
                ThrowOnVersionRegister = true,
                ThrowOnVersion = 7,
            };
            await using var services = BuildServices(isolated.ConnectionString, hooks);

            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(services));
            hooks.ThrowOnVersionRegister = false;

            var eventA = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var eventB = Guid.Parse("22222222-2222-4222-8222-222222222222");
            var tsA = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            // Intervalo de chunk = 6h → fuerza segundo chunk.
            var tsB = tsA.AddHours(12);

            await using var connection = new NpgsqlConnection(isolated.ConnectionString);
            await connection.OpenAsync();

            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO telemetry_events (
                        "EventId", "VehicleId", "DriverId", "Timestamp",
                        "Latitude", "Longitude", "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "CapturedAt")
                    VALUES
                        (@e1, '', NULL, @t1, 1, 1, 10, NULL, NULL, NOW()),
                        (@e2, '', NULL, @t2, 2, 2, 20, NULL, NULL, NOW());
                    """;
                seed.Parameters.AddWithValue("e1", eventA);
                seed.Parameters.AddWithValue("e2", eventB);
                seed.Parameters.AddWithValue("t1", tsA);
                seed.Parameters.AddWithValue("t2", tsB);
                await seed.ExecuteNonQueryAsync();
            }

            long beforeCount;
            await using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM telemetry_events;";
                beforeCount = Convert.ToInt64(await count.ExecuteScalarAsync());
            }

            await DatabaseInitializer.InitializeAsync(services);

            await using (var verify = connection.CreateCommand())
            {
                verify.CommandText = """
                    SELECT "EventId", device_id
                    FROM telemetry_events
                    WHERE "EventId" IN (@e1, @e2)
                    ORDER BY "EventId";
                    """;
                verify.Parameters.AddWithValue("e1", eventA);
                verify.Parameters.AddWithValue("e2", eventB);
                await using var reader = await verify.ExecuteReaderAsync();
                var ids = new List<(Guid EventId, Guid DeviceId)>();
                while (await reader.ReadAsync())
                {
                    Assert.False(reader.IsDBNull(1));
                    ids.Add((reader.GetGuid(0), reader.GetGuid(1)));
                }

                Assert.Equal(2, ids.Count);
                Assert.NotEqual(ids[0].DeviceId, ids[1].DeviceId);
                Assert.All(ids, row => Assert.NotEqual(Guid.Empty, row.DeviceId));
            }

            await using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM telemetry_events;";
                Assert.Equal(beforeCount, Convert.ToInt64(await count.ExecuteScalarAsync()));
            }

            await using (var nulls = connection.CreateCommand())
            {
                nulls.CommandText = "SELECT COUNT(*) FROM telemetry_events WHERE device_id IS NULL;";
                Assert.Equal(0, Convert.ToInt32(await nulls.ExecuteScalarAsync()));
            }

            // Reentrada segura: no deja null ni duplica filas.
            await DatabaseInitializer.InitializeAsync(services);
            await using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM telemetry_events;";
                Assert.Equal(beforeCount, Convert.ToInt64(await count.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    private static ServiceProvider BuildServices(string connectionString, TestSchemaMigrationHooks hooks)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<ISchemaMigrationHooks>(hooks);
        return services.BuildServiceProvider();
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
