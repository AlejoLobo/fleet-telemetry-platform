using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;

// Política de ejecución del DDL automático de TimescaleDB.
namespace FleetTelemetry.Infrastructure.Persistence;

public static class DatabaseInitializationPolicy
{
    public static bool ShouldRun(IHostEnvironment environment, TimescaleDbOptions options) =>
        environment.IsDevelopment() || options.AllowAutoSchemaInitialization;
}
