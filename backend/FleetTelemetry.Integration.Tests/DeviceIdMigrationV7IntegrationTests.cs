using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetTelemetry.Integration.Tests;

/// <summary>
/// Valida migración v7: secuencia VH-### y asignación de device_id huérfano
/// por claves estables (EventId + Timestamp) sin usar ctid en hypertables.
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
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        var eventA = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var eventB = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var tsA = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Intervalo de chunk = 6h → fuerza segundo chunk.
        var tsB = tsA.AddHours(12);

        await using (var prepare = connection.CreateCommand())
        {
            prepare.CommandText = """
                ALTER TABLE telemetry_events ALTER COLUMN device_id DROP NOT NULL;
                DELETE FROM telemetry_events
                WHERE "EventId" IN (@e1, @e2);
                """;
            prepare.Parameters.AddWithValue("e1", eventA);
            prepare.Parameters.AddWithValue("e2", eventB);
            await prepare.ExecuteNonQueryAsync();
        }

        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO telemetry_events (
                    "EventId", device_id, "DriverId", "Timestamp",
                    "Latitude", "Longitude", "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "CapturedAt")
                VALUES
                    (@e1, NULL, NULL, @t1, 1, 1, 10, NULL, NULL, NOW()),
                    (@e2, NULL, NULL, @t2, 2, 2, 20, NULL, NULL, NOW());
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
            count.CommandText = """
                SELECT COUNT(*) FROM telemetry_events
                WHERE "EventId" IN (@e1, @e2);
                """;
            count.Parameters.AddWithValue("e1", eventA);
            count.Parameters.AddWithValue("e2", eventB);
            beforeCount = Convert.ToInt64(await count.ExecuteScalarAsync());
        }

        Assert.Equal(2, beforeCount);

        // Mismo criterio de producción: EventId + Timestamp (no ctid).
        await using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText = """
                DO $$
                DECLARE
                    r RECORD;
                    assigned uuid;
                    candidate text;
                    suffix int;
                BEGIN
                    FOR r IN
                        SELECT "EventId" AS k_event_id, "Timestamp" AS k_timestamp
                        FROM telemetry_events
                        WHERE device_id IS NULL
                          AND "EventId" IN (
                              '11111111-1111-4111-8111-111111111111'::uuid,
                              '22222222-2222-4222-8222-222222222222'::uuid)
                    LOOP
                        assigned := gen_random_uuid();
                        candidate := 'orphan-' || substr(assigned::text, 1, 8);
                        suffix := 0;

                        LOOP
                            BEGIN
                                INSERT INTO fleet_devices (device_id, vehicle_name, created_at, updated_at)
                                VALUES (assigned, candidate, NOW(), NOW());
                                EXIT;
                            EXCEPTION WHEN unique_violation THEN
                                IF EXISTS (SELECT 1 FROM fleet_devices d WHERE d.device_id = assigned) THEN
                                    EXIT;
                                END IF;
                                suffix := suffix + 1;
                                candidate := 'orphan-' || substr(assigned::text, 1, 8) || '-' || suffix::text;
                            END;
                        END LOOP;

                        UPDATE telemetry_events
                        SET device_id = assigned
                        WHERE device_id IS NULL
                          AND "EventId" = r.k_event_id
                          AND "Timestamp" = r.k_timestamp;
                    END LOOP;
                END $$;
                """;
            await migrate.ExecuteNonQueryAsync();
        }

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
            count.CommandText = """
                SELECT COUNT(*) FROM telemetry_events
                WHERE "EventId" IN (@e1, @e2);
                """;
            count.Parameters.AddWithValue("e1", eventA);
            count.Parameters.AddWithValue("e2", eventB);
            Assert.Equal(beforeCount, Convert.ToInt64(await count.ExecuteScalarAsync()));
        }

        await using (var nulls = connection.CreateCommand())
        {
            nulls.CommandText = """
                SELECT COUNT(*) FROM telemetry_events
                WHERE "EventId" IN (@e1, @e2) AND device_id IS NULL;
                """;
            nulls.Parameters.AddWithValue("e1", eventA);
            nulls.Parameters.AddWithValue("e2", eventB);
            Assert.Equal(0, Convert.ToInt32(await nulls.ExecuteScalarAsync()));
        }

        // Reentrada: no modifica filas ya asignadas ni cambia el conteo.
        await using (var migrateAgain = connection.CreateCommand())
        {
            migrateAgain.CommandText = """
                DO $$
                DECLARE
                    r RECORD;
                    assigned uuid;
                BEGIN
                    FOR r IN
                        SELECT "EventId" AS k_event_id, "Timestamp" AS k_timestamp
                        FROM telemetry_events
                        WHERE device_id IS NULL
                          AND "EventId" IN (
                              '11111111-1111-4111-8111-111111111111'::uuid,
                              '22222222-2222-4222-8222-222222222222'::uuid)
                    LOOP
                        assigned := gen_random_uuid();
                        UPDATE telemetry_events
                        SET device_id = assigned
                        WHERE device_id IS NULL
                          AND "EventId" = r.k_event_id
                          AND "Timestamp" = r.k_timestamp;
                    END LOOP;
                END $$;
                """;
            await migrateAgain.ExecuteNonQueryAsync();
        }

        await using (var restore = connection.CreateCommand())
        {
            restore.CommandText = "ALTER TABLE telemetry_events ALTER COLUMN device_id SET NOT NULL;";
            await restore.ExecuteNonQueryAsync();
        }
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
}
