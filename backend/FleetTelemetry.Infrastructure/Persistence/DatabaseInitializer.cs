using System.Data.Common;
using FleetTelemetry.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Inicialización del esquema TimescaleDB.
namespace FleetTelemetry.Infrastructure.Persistence;

// Crea extensiones, tablas e índices si no existen.
public static class DatabaseInitializer
{
    private const long SchemaAdvisoryLockKey = 742001;
    private const int ReadModelSchemaVersion = 2;
    private const int ReadModelVerificationSchemaVersion = 3;

    // Ejecuta DDL idempotente para hypertables e índices.
    public static async Task InitializeAsync(
        IServiceProvider serviceProvider,
        bool useAdvisoryLock = true,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<FleetDbContext>>();
        var migrationHooks = scope.ServiceProvider.GetService<ISchemaMigrationHooks>()
            ?? NoOpSchemaMigrationHooks.Instance;

        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        Exception? initializationException = null;

        try
        {
            if (useAdvisoryLock)
                await SetAdvisoryLockAsync(connection, acquire: true, cancellationToken);

            try
            {
                await InitializeBaseSchemaAsync(connection, logger, cancellationToken);
                var v2AppliedNow = await ApplyReadModelMigrationV2Async(
                    connection,
                    logger,
                    migrationHooks,
                    cancellationToken);
                await ApplyReadModelVerificationV3Async(
                    connection,
                    logger,
                    migrationHooks,
                    v2AppliedNow,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                initializationException = ex;
                throw;
            }
        }
        finally
        {
            if (useAdvisoryLock)
            {
                try
                {
                    await SetAdvisoryLockAsync(connection, acquire: false, CancellationToken.None);
                }
                catch (Exception unlockException)
                {
                    logger.LogError(
                        unlockException,
                        "No se pudo liberar pg_advisory_unlock({LockKey}).",
                        SchemaAdvisoryLockKey);

                    if (initializationException is not null)
                        throw initializationException;

                    throw;
                }
            }

            await connection.CloseAsync();
        }
    }

    private static async Task SetAdvisoryLockAsync(
        DbConnection connection,
        bool acquire,
        CancellationToken cancellationToken)
    {
        var function = acquire ? "pg_advisory_lock" : "pg_advisory_unlock";

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {function}({SchemaAdvisoryLockKey});";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task InitializeBaseSchemaAsync(
        DbConnection connection,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            connection,
            "CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;",
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS processed_events (
                "EventId" uuid NOT NULL,
                "ProcessedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_processed_events" PRIMARY KEY ("EventId")
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
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

        await ExecuteSqlAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS ix_fleet_alerts_vehicle_created
            ON fleet_alerts ("VehicleId", "CreatedAt" DESC);
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
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

        await ExecuteSqlAsync(
            connection,
            """
            SELECT create_hypertable('telemetry_events', 'Timestamp', if_not_exists => TRUE);
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS ix_telemetry_events_vehicle_timestamp
            ON telemetry_events ("VehicleId", "Timestamp" DESC);
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS schema_versions (
                "Version" integer NOT NULL,
                "AppliedAt" timestamp with time zone NOT NULL,
                "Description" character varying(256) NOT NULL,
                CONSTRAINT "PK_schema_versions" PRIMARY KEY ("Version")
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
            VALUES (1, NOW(), 'Initial TimescaleDB schema with hypertable telemetry_events')
            ON CONFLICT ("Version") DO NOTHING;
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            ALTER TABLE telemetry_events
            ADD COLUMN IF NOT EXISTS "LocationSource" character varying(16) NOT NULL DEFAULT 'gps';
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS ix_telemetry_events_vehicle_timestamp_event
            ON telemetry_events ("VehicleId", "Timestamp" DESC, "EventId" DESC);
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS fleet_connectivity_watermark (
                "Id" integer NOT NULL DEFAULT 1,
                "PreviousOnlineThreshold" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_fleet_connectivity_watermark" PRIMARY KEY ("Id")
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS fleet_offline_publish_markers (
                "VehicleId" character varying(64) NOT NULL,
                "LastEventId" uuid NOT NULL,
                "StatusEvaluatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_fleet_offline_publish_markers" PRIMARY KEY ("VehicleId")
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS ix_fleet_vehicle_state_last_timestamp_vehicle
            ON fleet_vehicle_state ("LastTimestamp" ASC, "VehicleId" ASC);
            """,
            cancellationToken);

        logger.LogInformation("TimescaleDB base schema initialized successfully.");
    }

    private static async Task<bool> ApplyReadModelMigrationV2Async(
        DbConnection connection,
        ILogger logger,
        ISchemaMigrationHooks migrationHooks,
        CancellationToken cancellationToken)
    {
        if (await SchemaVersionExistsAsync(connection, ReadModelSchemaVersion, cancellationToken))
        {
            logger.LogInformation(
                "Read model migration v{Version} already applied; preserving historical record.",
                ReadModelSchemaVersion);
            return false;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureFleetVehicleStateSchemaAsync(connection, transaction, cancellationToken);
            await migrationHooks.OnBackfillStartingAsync(cancellationToken);
            await ExecuteDeterministicBackfillAsync(connection, transaction, cancellationToken);
            await migrationHooks.OnBeforeRegisterVersionAsync(ReadModelSchemaVersion, cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (2, NOW(), 'fleet_vehicle_state read model with deterministic backfill');
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Read model migration v{Version} applied successfully.", ReadModelSchemaVersion);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ApplyReadModelVerificationV3Async(
        DbConnection connection,
        ILogger logger,
        ISchemaMigrationHooks migrationHooks,
        bool v2AppliedNow,
        CancellationToken cancellationToken)
    {
        if (await SchemaVersionExistsAsync(connection, ReadModelVerificationSchemaVersion, cancellationToken))
        {
            logger.LogInformation(
                "Read model verification v{Version} already applied; skipping repair.",
                ReadModelVerificationSchemaVersion);
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureFleetVehicleStateSchemaAsync(connection, transaction, cancellationToken);

            if (v2AppliedNow)
            {
                logger.LogInformation(
                    "Read model verification v{Version} skipping repair backfill because v2 was applied in this run.",
                    ReadModelVerificationSchemaVersion);
            }
            else
            {
                await migrationHooks.OnBackfillStartingAsync(cancellationToken);
                await ExecuteDeterministicBackfillAsync(connection, transaction, cancellationToken);
            }

            await migrationHooks.OnBeforeRegisterVersionAsync(
                ReadModelVerificationSchemaVersion,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (3, NOW(), 'fleet_vehicle_state verification and deterministic repair');
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Read model verification v{Version} applied successfully.",
                ReadModelVerificationSchemaVersion);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureFleetVehicleStateSchemaAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS fleet_vehicle_state (
                "VehicleId" character varying(64) NOT NULL,
                "LastEventId" uuid NOT NULL,
                "DriverId" character varying(64),
                "LastTimestamp" timestamp with time zone NOT NULL,
                "Latitude" double precision NOT NULL,
                "Longitude" double precision NOT NULL,
                "SpeedKmh" double precision NOT NULL,
                "FuelLevelPercent" double precision,
                "BatteryPercent" double precision,
                "LocationSource" character varying(16) NOT NULL DEFAULT 'gps',
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_fleet_vehicle_state" PRIMARY KEY ("VehicleId")
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS ix_fleet_vehicle_state_last_timestamp
            ON fleet_vehicle_state ("LastTimestamp" DESC);
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS ix_fleet_vehicle_state_location_source_timestamp
            ON fleet_vehicle_state ("LocationSource", "LastTimestamp" DESC);
            """,
            cancellationToken);
    }

    private static async Task ExecuteDeterministicBackfillAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            INSERT INTO fleet_vehicle_state (
                "VehicleId", "LastEventId", "DriverId", "LastTimestamp", "Latitude", "Longitude",
                "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "LocationSource", "UpdatedAt"
            )
            SELECT
                latest."VehicleId",
                latest."EventId",
                latest."DriverId",
                latest."Timestamp",
                latest."Latitude",
                latest."Longitude",
                latest."SpeedKmh",
                latest."FuelLevelPercent",
                latest."BatteryPercent",
                latest."LocationSource",
                NOW()
            FROM (
                SELECT DISTINCT ON ("VehicleId")
                    "EventId", "VehicleId", "DriverId", "Timestamp", "Latitude", "Longitude",
                    "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "LocationSource"
                FROM telemetry_events
                ORDER BY "VehicleId", "Timestamp" DESC, "EventId" DESC
            ) AS latest
            ON CONFLICT ("VehicleId") DO UPDATE
            SET
                "LastEventId" = EXCLUDED."LastEventId",
                "DriverId" = EXCLUDED."DriverId",
                "LastTimestamp" = EXCLUDED."LastTimestamp",
                "Latitude" = EXCLUDED."Latitude",
                "Longitude" = EXCLUDED."Longitude",
                "SpeedKmh" = EXCLUDED."SpeedKmh",
                "FuelLevelPercent" = EXCLUDED."FuelLevelPercent",
                "BatteryPercent" = EXCLUDED."BatteryPercent",
                "LocationSource" = EXCLUDED."LocationSource",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            WHERE EXCLUDED."LastTimestamp" > fleet_vehicle_state."LastTimestamp"
               OR (
                   EXCLUDED."LastTimestamp" = fleet_vehicle_state."LastTimestamp"
                   AND EXCLUDED."LastEventId" > fleet_vehicle_state."LastEventId"
               );
            """,
            cancellationToken);
    }

    private static async Task<bool> SchemaVersionExistsAsync(
        DbConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM schema_versions
                WHERE "Version" = @version
            );
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "version";
        parameter.Value = version;
        command.Parameters.Add(parameter);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ExecuteSqlAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteSqlAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
