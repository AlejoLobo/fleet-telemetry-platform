using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetTelemetry.Integration.Tests;

// Valida migración v5: chunks, compresión, retención, agregado horario y cleanup.
public class TimescaleMaintenanceMigrationV5IntegrationTests : IAsyncLifetime
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
    public async Task Migration_v5_registers_policies_and_remains_idempotent()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        Assert.True(await SchemaVersionExistsAsync(connection, 5));
        Assert.True(await HypertableExistsAsync(connection));
        Assert.Equal(TimeSpan.FromHours(6), await GetChunkIntervalAsync(connection));
        Assert.True(await CompressionEnabledAsync(connection));
        Assert.Equal(1, await CountJobsAsync(connection, "policy_compression", "telemetry_events"));
        Assert.Equal(1, await CountJobsAsync(connection, "policy_retention", "telemetry_events"));
        Assert.Equal(1, await CountContinuousAggregateRefreshPoliciesAsync(connection, "telemetry_hourly"));
        Assert.Equal(1, await CountJobsByProcAsync(connection, "cleanup_processed_events"));
        Assert.True(await IndexExistsAsync(connection, "ix_processed_events_processed_at"));

        await DatabaseInitializer.InitializeAsync(_services);

        Assert.Equal(1, await CountJobsAsync(connection, "policy_compression", "telemetry_events"));
        Assert.Equal(1, await CountJobsAsync(connection, "policy_retention", "telemetry_events"));
        Assert.Equal(1, await CountContinuousAggregateRefreshPoliciesAsync(connection, "telemetry_hourly"));
        Assert.Equal(1, await CountJobsByProcAsync(connection, "cleanup_processed_events"));
        Assert.Equal(1, await CountSchemaVersionRowsAsync(connection, 5));
    }

    [Fact]
    public async Task Cleanup_processed_events_removes_old_and_keeps_recent()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        var oldEventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var recentEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO processed_events ("EventId", "ProcessedAt")
                VALUES
                    (@oldId, NOW() - INTERVAL '121 days'),
                    (@recentId, NOW())
                ON CONFLICT ("EventId") DO UPDATE
                SET "ProcessedAt" = EXCLUDED."ProcessedAt";
                """;
            insert.Parameters.AddWithValue("oldId", oldEventId);
            insert.Parameters.AddWithValue("recentId", recentEventId);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.CommandText = "SELECT cleanup_processed_events(0, '{}'::jsonb);";
            await cleanup.ExecuteNonQueryAsync();
        }

        Assert.False(await ProcessedEventExistsAsync(connection, oldEventId));
        Assert.True(await ProcessedEventExistsAsync(connection, recentEventId));
    }

    [Fact]
    public async Task Telemetry_hourly_refresh_computes_sample_count_and_speed_stats()
    {
        await DatabaseInitializer.InitializeAsync(_services);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        var bucketStart = DateTimeOffset.UtcNow.ToOffset(TimeSpan.Zero);
        bucketStart = new DateTimeOffset(
            bucketStart.Year,
            bucketStart.Month,
            bucketStart.Day,
            bucketStart.Hour,
            0,
            0,
            TimeSpan.Zero);

        var vehicleId = "VH-HOURLY-001";
        var eventA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var eventB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO telemetry_events (
                    "EventId", "VehicleId", "DriverId", "Timestamp",
                    "Latitude", "Longitude", "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "CapturedAt")
                VALUES
                    (@eventA, @vehicleId, 'DRV-1', @tsA, 4.65, -74.08, 40, 50, 80, NOW()),
                    (@eventB, @vehicleId, 'DRV-1', @tsB, 4.65, -74.08, 60, 40, 70, NOW())
                ON CONFLICT ("EventId", "Timestamp") DO NOTHING;
                """;
            insert.Parameters.AddWithValue("eventA", eventA);
            insert.Parameters.AddWithValue("eventB", eventB);
            insert.Parameters.AddWithValue("vehicleId", vehicleId);
            insert.Parameters.AddWithValue("tsA", bucketStart.AddMinutes(10));
            insert.Parameters.AddWithValue("tsB", bucketStart.AddMinutes(20));
            await insert.ExecuteNonQueryAsync();
        }

        await using (var refresh = connection.CreateCommand())
        {
            refresh.CommandText = """
                CALL refresh_continuous_aggregate(
                    'telemetry_hourly',
                    @windowStart,
                    @windowEnd);
                """;
            refresh.Parameters.AddWithValue("windowStart", bucketStart.AddHours(-1));
            refresh.Parameters.AddWithValue("windowEnd", bucketStart.AddHours(2));
            await refresh.ExecuteNonQueryAsync();
        }

        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT "SampleCount", "AverageSpeedKmh", "MaxSpeedKmh"
            FROM telemetry_hourly
            WHERE "VehicleId" = @vehicleId
              AND "Bucket" = @bucket;
            """;
        query.Parameters.AddWithValue("vehicleId", vehicleId);
        query.Parameters.AddWithValue("bucket", bucketStart);

        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(50d, reader.GetDouble(1), precision: 5);
        Assert.Equal(60d, reader.GetDouble(2), precision: 5);
        Assert.False(await reader.ReadAsync());
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

    private static async Task<int> CountSchemaVersionRowsAsync(NpgsqlConnection connection, int version)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM schema_versions
            WHERE "Version" = @version;
            """,
            connection);
        command.Parameters.AddWithValue("version", version);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> HypertableExistsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM timescaledb_information.hypertables
                WHERE hypertable_name = 'telemetry_events'
            );
            """,
            connection);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<TimeSpan> GetChunkIntervalAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT time_interval
            FROM timescaledb_information.dimensions
            WHERE hypertable_name = 'telemetry_events'
              AND dimension_type = 'Time';
            """,
            connection);
        var value = await command.ExecuteScalarAsync();
        return (TimeSpan)(value ?? TimeSpan.Zero);
    }

    private static async Task<bool> CompressionEnabledAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM timescaledb_information.compression_settings
                WHERE hypertable_name = 'telemetry_events'
                  AND attname = 'VehicleId'
                  AND segmentby_column_index = 1
            )
            AND EXISTS (
                SELECT 1
                FROM timescaledb_information.compression_settings
                WHERE hypertable_name = 'telemetry_events'
                  AND attname = 'Timestamp'
                  AND orderby_column_index = 1
                  AND orderby_asc = FALSE
            );
            """,
            connection);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> CountJobsAsync(
        NpgsqlConnection connection,
        string procName,
        string hypertableName)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM timescaledb_information.jobs
            WHERE proc_name = @procName
              AND hypertable_name = @hypertableName;
            """,
            connection);
        command.Parameters.AddWithValue("procName", procName);
        command.Parameters.AddWithValue("hypertableName", hypertableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountJobsByProcAsync(NpgsqlConnection connection, string procName)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM timescaledb_information.jobs
            WHERE proc_name = @procName;
            """,
            connection);
        command.Parameters.AddWithValue("procName", procName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountContinuousAggregateRefreshPoliciesAsync(
        NpgsqlConnection connection,
        string viewName)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM timescaledb_information.jobs j
            JOIN timescaledb_information.continuous_aggregates c
              ON c.materialization_hypertable_name = j.hypertable_name
             AND c.materialization_hypertable_schema = j.hypertable_schema
            WHERE j.proc_name = 'policy_refresh_continuous_aggregate'
              AND c.view_name = @viewName;
            """,
            connection);
        command.Parameters.AddWithValue("viewName", viewName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string indexName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = @indexName);",
            connection);
        command.Parameters.AddWithValue("indexName", indexName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> ProcessedEventExistsAsync(NpgsqlConnection connection, Guid eventId)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM processed_events WHERE "EventId" = @eventId
            );
            """,
            connection);
        command.Parameters.AddWithValue("eventId", eventId);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }
}
