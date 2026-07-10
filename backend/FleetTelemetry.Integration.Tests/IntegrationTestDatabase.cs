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
            ConnectionString = externalConnection;
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

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
