using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Inicialización del esquema TimescaleDB.
namespace FleetTelemetry.Infrastructure.Persistence;

// Crea extensiones, tablas e índices si no existen.
public static class DatabaseInitializer
{
    // Ejecuta DDL idempotente para hypertables e índices.
    public static async Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<FleetDbContext>>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS processed_events (
                "EventId" uuid NOT NULL,
                "ProcessedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_processed_events" PRIMARY KEY ("EventId")
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS fleet_alerts (
                "AlertId" uuid NOT NULL,
                "VehicleId" character varying(64) NOT NULL,
                "AlertType" character varying(64) NOT NULL,
                "Severity" character varying(32) NOT NULL,
                "Message" character varying(512) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "IsAcknowledged" boolean NOT NULL,
                CONSTRAINT "PK_fleet_alerts" PRIMARY KEY ("AlertId")
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS ix_fleet_alerts_vehicle_created
            ON fleet_alerts ("VehicleId", "CreatedAt" DESC);
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS telemetry_events (
                "EventId" uuid NOT NULL,
                "VehicleId" character varying(64) NOT NULL,
                "DriverId" character varying(64),
                "Timestamp" timestamp with time zone NOT NULL,
                "Latitude" double precision NOT NULL,
                "Longitude" double precision NOT NULL,
                "SpeedKmh" double precision NOT NULL,
                "FuelLevelPercent" double precision,
                "BatteryPercent" double precision,
                "CapturedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_telemetry_events" PRIMARY KEY ("EventId", "Timestamp")
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            SELECT create_hypertable('telemetry_events', 'Timestamp', if_not_exists => TRUE);
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS ix_telemetry_events_vehicle_timestamp
            ON telemetry_events ("VehicleId", "Timestamp" DESC);
            """,
            cancellationToken);

        logger.LogInformation("TimescaleDB schema initialized successfully.");
    }
}
