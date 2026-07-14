using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetTelemetry.Integration.Tests;

// Valida inicialización concurrente del esquema TimescaleDB.
public class DatabaseInitializerIntegrationTests : IAsyncLifetime
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
    public async Task Concurrent_initialization_completes_without_errors_and_leaves_single_schema()
    {
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => DatabaseInitializer.InitializeAsync(_services))
            .ToArray();

        var exceptions = await Task.WhenAll(tasks.Select(CaptureExceptionAsync));
        Assert.All(exceptions, Assert.Null);

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        Assert.True(await TimescaleExtensionExistsAsync(connection));
        Assert.Equal(
            6,
            await CountUserTablesAsync(
                connection,
                "processed_events",
                "fleet_alerts",
                "telemetry_events",
                "schema_versions",
                "fleet_vehicle_state",
                "fleet_alert_states"));
        Assert.True(await HypertableExistsAsync(connection));
        Assert.True(await IndexExistsAsync(connection, "ix_fleet_alerts_vehicle_created"));
        Assert.True(await IndexExistsAsync(connection, "ix_telemetry_events_vehicle_timestamp"));
        Assert.True(await IndexExistsAsync(connection, "ix_fleet_alert_states_active_condition"));
        Assert.True(await SchemaVersionExistsAsync(connection, 4));
        Assert.Equal(0, await CountActiveAdvisoryLocksAsync(connection));
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

    private static async Task<bool> TimescaleExtensionExistsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb');",
            connection);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> CountUserTablesAsync(NpgsqlConnection connection, params string[] tableNames)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = ANY(@tables);
            """,
            connection);
        command.Parameters.AddWithValue("tables", tableNames);
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

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string indexName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = @indexName);",
            connection);
        command.Parameters.AddWithValue("indexName", indexName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> CountActiveAdvisoryLocksAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM pg_locks
            WHERE locktype = 'advisory'
              AND classid = 0
              AND objid = 742001;
            """,
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
