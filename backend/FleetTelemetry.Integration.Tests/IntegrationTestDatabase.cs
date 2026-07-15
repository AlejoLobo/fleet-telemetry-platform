using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace FleetTelemetry.Integration.Tests;

// Provee TimescaleDB para integración: Testcontainers por defecto o Compose local vía variable de entorno.
public sealed class IntegrationTestDatabase : IAsyncLifetime
{
    public const string ConnectionStringEnvVar = "FLEET_INTEGRATION_DB_CONNECTION";

    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public bool UsesExternalDatabase =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvVar));

    public async Task InitializeAsync()
    {
        var externalConnection = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (!string.IsNullOrWhiteSpace(externalConnection))
        {
            ConnectionString = externalConnection.Trim();
            await EnsureSchemaInitializedAsync();
            return;
        }

        _container = new PostgreSqlBuilder()
            .WithImage("timescale/timescaledb:2.17.2-pg16")
            .WithDatabase("fleet")
            .WithUsername("fleet")
            .WithPassword("fleet")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        await EnsureSchemaInitializedAsync();
    }

    /// <summary>
    /// Arranca TimescaleDB sin aplicar migraciones (útil para interrumpir v7 a mitad).
    /// </summary>
    public async Task InitializeEmptyAsync()
    {
        var externalConnection = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (!string.IsNullOrWhiteSpace(externalConnection))
        {
            ConnectionString = externalConnection.Trim();
            return;
        }

        _container = new PostgreSqlBuilder()
            .WithImage("timescale/timescaledb:2.17.2-pg16")
            .WithDatabase("fleet")
            .WithUsername("fleet")
            .WithPassword("fleet")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    /// <summary>
    /// Deja public vacío (sin schema_versions). Necesario en CI con DB compartida
    /// para poder reaplicar migraciones e interrumpir v7.
    /// </summary>
    public async Task ResetPublicSchemaAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public;
            GRANT ALL ON SCHEMA public TO CURRENT_USER;
            GRANT ALL ON SCHEMA public TO public;
            -- timescaledb ya está cargado a nivel de base; no recrear la extensión.
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private static async Task EnsureSchemaInitializedAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(connectionString));

        await using var provider = services.BuildServiceProvider();
        await DatabaseInitializer.InitializeAsync(provider);
    }

    private Task EnsureSchemaInitializedAsync() =>
        EnsureSchemaInitializedAsync(ConnectionString);
}
