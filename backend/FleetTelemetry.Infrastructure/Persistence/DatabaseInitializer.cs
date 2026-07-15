using System.Data.Common;
using FleetTelemetry.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private const long SchemaAdvisoryLockKey = 742001;
    private const int ReadModelSchemaVersion = 2;
    private const int ReadModelVerificationSchemaVersion = 3;
    private const int AlertStateSchemaVersion = 4;
    private const int TimescaleMaintenanceSchemaVersion = 5;
    private const int FleetDevicesSchemaVersion = 6;
    private const int DeviceIdPersistenceSchemaVersion = 7;

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
                await ApplyAlertStateMigrationV4Async(
                    connection,
                    logger,
                    migrationHooks,
                    cancellationToken);
                await ApplyTimescaleMaintenanceMigrationV5Async(
                    connection,
                    logger,
                    migrationHooks,
                    cancellationToken);
                await ApplyFleetDevicesMigrationV6Async(
                    connection,
                    logger,
                    migrationHooks,
                    cancellationToken);
                await ApplyDeviceIdPersistenceMigrationV7Async(
                    connection,
                    logger,
                    migrationHooks,
                    cancellationToken);
                await using var ensureTransaction = await connection.BeginTransactionAsync(cancellationToken);
                await EnsureFleetVehicleStateSchemaAsync(connection, ensureTransaction, cancellationToken);
                await EnsureFleetAlertStatesSchemaAsync(connection, ensureTransaction, cancellationToken);
                await EnsureFleetOfflinePublishMarkersSchemaAsync(connection, ensureTransaction, cancellationToken);
                await EnsureFleetDevicesSchemaAsync(connection, ensureTransaction, cancellationToken);
                await EnsureTelemetryEventsDeviceIdSchemaAsync(connection, ensureTransaction, cancellationToken);
                await EnsureFleetAlertsDeviceIdSchemaAsync(connection, ensureTransaction, cancellationToken);
                await ensureTransaction.CommitAsync(cancellationToken);
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

        // Tras v7 la columna es device_id; no recrear índices sobre VehicleId inexistente.
        if (await ColumnExistsAsync(connection, null, "fleet_alerts", "VehicleId", cancellationToken))
        {
            await ExecuteSqlAsync(
                connection,
                """
                CREATE INDEX IF NOT EXISTS ix_fleet_alerts_vehicle_created
                ON fleet_alerts ("VehicleId", "CreatedAt" DESC);
                """,
                cancellationToken);
        }

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

        if (await ColumnExistsAsync(connection, null, "telemetry_events", "VehicleId", cancellationToken))
        {
            await ExecuteSqlAsync(
                connection,
                """
                CREATE INDEX IF NOT EXISTS ix_telemetry_events_vehicle_timestamp
                ON telemetry_events ("VehicleId", "Timestamp" DESC);
                """,
                cancellationToken);
        }

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

        if (await ColumnExistsAsync(connection, null, "telemetry_events", "VehicleId", cancellationToken))
        {
            await ExecuteSqlAsync(
                connection,
                """
                CREATE INDEX IF NOT EXISTS ix_telemetry_events_vehicle_timestamp_event
                ON telemetry_events ("VehicleId", "Timestamp" DESC, "EventId" DESC);
                """,
                cancellationToken);
        }

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
        var tableExists = await TableExistsAsync(connection, transaction, "fleet_vehicle_state", cancellationToken);
        var hasDeviceId = tableExists
            && await ColumnExistsAsync(connection, transaction, "fleet_vehicle_state", "device_id", cancellationToken);
        var hasLegacyVehicleId = tableExists
            && await ColumnExistsAsync(connection, transaction, "fleet_vehicle_state", "VehicleId", cancellationToken);

        if (!tableExists)
        {
            if (await SchemaVersionExistsAsync(connection, DeviceIdPersistenceSchemaVersion, cancellationToken))
            {
                await ExecuteSqlAsync(
                    connection,
                    transaction,
                    """
                    CREATE TABLE fleet_vehicle_state (
                        device_id UUID NOT NULL,
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
                        CONSTRAINT "PK_fleet_vehicle_state" PRIMARY KEY (device_id)
                    );
                    """,
                    cancellationToken);
                hasDeviceId = true;
            }
            else
            {
                await ExecuteSqlAsync(
                    connection,
                    transaction,
                    """
                    CREATE TABLE fleet_vehicle_state (
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
                hasLegacyVehicleId = true;
            }
        }

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

        if (hasDeviceId)
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_fleet_vehicle_state_last_timestamp_device
                ON fleet_vehicle_state ("LastTimestamp" ASC, device_id ASC);
                """,
                cancellationToken);
        }
        else if (hasLegacyVehicleId)
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_fleet_vehicle_state_last_timestamp_vehicle
                ON fleet_vehicle_state ("LastTimestamp" ASC, "VehicleId" ASC);
                """,
                cancellationToken);
        }
    }

    private static async Task ApplyAlertStateMigrationV4Async(
        DbConnection connection,
        ILogger logger,
        ISchemaMigrationHooks migrationHooks,
        CancellationToken cancellationToken)
    {
        if (await SchemaVersionExistsAsync(connection, AlertStateSchemaVersion, cancellationToken))
        {
            logger.LogInformation(
                "Alert state migration v{Version} already applied; preserving historical alerts.",
                AlertStateSchemaVersion);
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureFleetAlertStatesSchemaAsync(connection, transaction, cancellationToken);
            await migrationHooks.OnBeforeRegisterVersionAsync(AlertStateSchemaVersion, cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (4, NOW(), 'fleet_alert_states active condition and cooldown');
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Alert state migration v{Version} applied successfully.", AlertStateSchemaVersion);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureFleetAlertStatesSchemaAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var tableExists = await TableExistsAsync(connection, transaction, "fleet_alert_states", cancellationToken);
        var hasDeviceId = tableExists
            && await ColumnExistsAsync(connection, transaction, "fleet_alert_states", "device_id", cancellationToken);

        if (!tableExists)
        {
            if (await SchemaVersionExistsAsync(connection, DeviceIdPersistenceSchemaVersion, cancellationToken))
            {
                await ExecuteSqlAsync(
                    connection,
                    transaction,
                    """
                    CREATE TABLE fleet_alert_states (
                        device_id UUID NOT NULL,
                        "AlertType" character varying(64) NOT NULL,
                        "IsActive" boolean NOT NULL,
                        "LastConditionAt" timestamp with time zone NOT NULL,
                        "LastAlertAt" timestamp with time zone,
                        "UpdatedAt" timestamp with time zone NOT NULL,
                        CONSTRAINT "PK_fleet_alert_states" PRIMARY KEY (device_id, "AlertType")
                    );
                    """,
                    cancellationToken);
            }
            else
            {
                await ExecuteSqlAsync(
                    connection,
                    transaction,
                    """
                    CREATE TABLE fleet_alert_states (
                        "VehicleId" character varying(64) NOT NULL,
                        "AlertType" character varying(64) NOT NULL,
                        "IsActive" boolean NOT NULL,
                        "LastConditionAt" timestamp with time zone NOT NULL,
                        "LastAlertAt" timestamp with time zone,
                        "UpdatedAt" timestamp with time zone NOT NULL,
                        CONSTRAINT "PK_fleet_alert_states" PRIMARY KEY ("VehicleId", "AlertType")
                    );
                    """,
                    cancellationToken);
            }
        }

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS ix_fleet_alert_states_active_condition
            ON fleet_alert_states ("IsActive", "LastConditionAt" DESC);
            """,
            cancellationToken);
    }

    // Políticas TimescaleDB: chunks, compresión, retención, agregado horario y limpieza de processed_events.
    private static async Task ApplyTimescaleMaintenanceMigrationV5Async(
        DbConnection connection,
        ILogger logger,
        ISchemaMigrationHooks migrationHooks,
        CancellationToken cancellationToken)
    {
        if (await SchemaVersionExistsAsync(connection, TimescaleMaintenanceSchemaVersion, cancellationToken))
        {
            logger.LogInformation(
                "Timescale maintenance migration v{Version} already applied; preserving policies.",
                TimescaleMaintenanceSchemaVersion);
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Chunk interval de 6 horas (válido sobre hypertable existente).
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                SELECT set_chunk_time_interval('telemetry_events', INTERVAL '6 hours');
                """,
                cancellationToken);

            // Compresión por vehículo, orden temporal descendente.
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                ALTER TABLE telemetry_events SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = '"VehicleId"',
                    timescaledb.compress_orderby = '"Timestamp" DESC'
                );
                """,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                SELECT add_compression_policy(
                    'telemetry_events',
                    compress_after => INTERVAL '7 days',
                    if_not_exists => TRUE);
                """,
                cancellationToken);

            // Retención solo sobre telemetría cruda (90 días).
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                SELECT add_retention_policy(
                    'telemetry_events',
                    drop_after => INTERVAL '90 days',
                    if_not_exists => TRUE);
                """,
                cancellationToken);

            if (!await ContinuousAggregateExistsAsync(connection, transaction, "telemetry_hourly", cancellationToken))
            {
                await ExecuteSqlAsync(
                    connection,
                    transaction,
                    """
                    CREATE MATERIALIZED VIEW telemetry_hourly
                    WITH (timescaledb.continuous) AS
                    SELECT
                        time_bucket(INTERVAL '1 hour', "Timestamp") AS "Bucket",
                        "VehicleId",
                        COUNT(*)::bigint AS "SampleCount",
                        AVG("SpeedKmh") AS "AverageSpeedKmh",
                        MAX("SpeedKmh") AS "MaxSpeedKmh",
                        MIN("FuelLevelPercent") AS "MinFuelLevelPercent",
                        MIN("BatteryPercent") AS "MinBatteryPercent"
                    FROM telemetry_events
                    GROUP BY 1, 2
                    WITH NO DATA;
                    """,
                    cancellationToken);
            }

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                SELECT add_continuous_aggregate_policy(
                    'telemetry_hourly',
                    start_offset => INTERVAL '30 days',
                    end_offset => INTERVAL '10 minutes',
                    schedule_interval => INTERVAL '15 minutes',
                    if_not_exists => TRUE);
                """,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_processed_events_processed_at
                ON processed_events ("ProcessedAt");
                """,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                CREATE OR REPLACE FUNCTION cleanup_processed_events(job_id integer, config jsonb)
                RETURNS void
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    DELETE FROM processed_events pe
                    WHERE pe.ctid = ANY (
                        ARRAY(
                            SELECT p.ctid
                            FROM processed_events p
                            WHERE p."ProcessedAt" < NOW() - INTERVAL '120 days'
                            ORDER BY p."ProcessedAt"
                            LIMIT 100000
                            FOR UPDATE SKIP LOCKED
                        )
                    );
                END;
                $$;
                """,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM timescaledb_information.jobs
                        WHERE proc_name = 'cleanup_processed_events'
                    ) THEN
                        PERFORM add_job('cleanup_processed_events', INTERVAL '5 minutes');
                    END IF;
                END $$;
                """,
                cancellationToken);

            await migrationHooks.OnBeforeRegisterVersionAsync(
                TimescaleMaintenanceSchemaVersion,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (
                    5,
                    NOW(),
                    'Timescale maintenance: 6h chunks, 7d compression, 90d retention, telemetry_hourly, processed_events cleanup');
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Timescale maintenance migration v{Version} applied successfully.",
                TimescaleMaintenanceSchemaVersion);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Registro de dispositivos estables y secuencia de nombres VH-###.
    private static async Task ApplyFleetDevicesMigrationV6Async(
        DbConnection connection,
        ILogger logger,
        ISchemaMigrationHooks migrationHooks,
        CancellationToken cancellationToken)
    {
        if (await SchemaVersionExistsAsync(connection, FleetDevicesSchemaVersion, cancellationToken))
        {
            logger.LogInformation(
                "Fleet devices migration v{Version} already applied; preserving registry.",
                FleetDevicesSchemaVersion);
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureFleetDevicesSchemaAsync(connection, transaction, cancellationToken);

            await migrationHooks.OnBeforeRegisterVersionAsync(
                FleetDevicesSchemaVersion,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (
                    6,
                    NOW(),
                    'Fleet devices registry: fleet_devices + fleet_vehicle_name_seq for VH-### allocation');
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Fleet devices migration v{Version} applied successfully.",
                FleetDevicesSchemaVersion);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureFleetDevicesSchemaAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS fleet_devices (
                device_id UUID NOT NULL,
                vehicle_name VARCHAR(100) NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                CONSTRAINT "PK_fleet_devices" PRIMARY KEY (device_id),
                CONSTRAINT "UQ_fleet_devices_vehicle_name" UNIQUE (vehicle_name)
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE SEQUENCE IF NOT EXISTS fleet_vehicle_name_seq
                AS BIGINT
                START WITH 1
                INCREMENT BY 1
                NO MINVALUE
                NO MAXVALUE
                CACHE 1;
            """,
            cancellationToken);
    }

    // Relaciona telemetría y estado operativo por DeviceId UUID; conserva VehicleId legado como vehicle_name.
    private static async Task ApplyDeviceIdPersistenceMigrationV7Async(
        DbConnection connection,
        ILogger logger,
        ISchemaMigrationHooks migrationHooks,
        CancellationToken cancellationToken)
    {
        if (await SchemaVersionExistsAsync(connection, DeviceIdPersistenceSchemaVersion, cancellationToken))
        {
            logger.LogInformation(
                "DeviceId persistence migration v{Version} already applied; preserving device_id schema.",
                DeviceIdPersistenceSchemaVersion);
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureFleetDevicesSchemaAsync(connection, transaction, cancellationToken);
            await BuildDeviceIdMigrationMapAsync(connection, transaction, cancellationToken);
            await InsertMigratedDevicesAsync(connection, transaction, cancellationToken);
            await SyncFleetVehicleNameSequenceAsync(connection, transaction, cancellationToken);

            await ConvertOperationalTableToDeviceIdAsync(
                connection,
                transaction,
                logger,
                "fleet_vehicle_state",
                primaryKeyColumns: ["device_id"],
                primaryKeyName: "PK_fleet_vehicle_state",
                dropIndexes: ["ix_fleet_vehicle_state_last_timestamp_vehicle"],
                createIndexesSql: """
                    CREATE INDEX IF NOT EXISTS ix_fleet_vehicle_state_last_timestamp_device
                    ON fleet_vehicle_state ("LastTimestamp" ASC, device_id ASC);
                    """,
                cancellationToken);

            await ConvertOperationalTableToDeviceIdAsync(
                connection,
                transaction,
                logger,
                "fleet_alerts",
                primaryKeyColumns: null,
                primaryKeyName: null,
                dropIndexes: ["ix_fleet_alerts_vehicle_created"],
                createIndexesSql: """
                    CREATE INDEX IF NOT EXISTS ix_fleet_alerts_device_created
                    ON fleet_alerts (device_id, "CreatedAt" DESC);
                    """,
                cancellationToken);

            await ConvertOperationalTableToDeviceIdAsync(
                connection,
                transaction,
                logger,
                "fleet_alert_states",
                primaryKeyColumns: ["device_id", "\"AlertType\""],
                primaryKeyName: "PK_fleet_alert_states",
                dropIndexes: null,
                createIndexesSql: null,
                cancellationToken);

            await ConvertOperationalTableToDeviceIdAsync(
                connection,
                transaction,
                logger,
                "fleet_offline_publish_markers",
                primaryKeyColumns: ["device_id"],
                primaryKeyName: "PK_fleet_offline_publish_markers",
                dropIndexes: null,
                createIndexesSql: null,
                cancellationToken);

            await ConvertTelemetryEventsToDeviceIdAsync(connection, transaction, logger, cancellationToken);
            await RecreateTelemetryHourlyAggregateAsync(connection, transaction, cancellationToken);

            await migrationHooks.OnBeforeRegisterVersionAsync(
                DeviceIdPersistenceSchemaVersion,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                INSERT INTO schema_versions ("Version", "AppliedAt", "Description")
                VALUES (
                    7,
                    NOW(),
                    'DeviceId UUID persistence: operational tables + telemetry keyed by device_id; legacy VehicleId kept as vehicle_name');
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "DeviceId persistence migration v{Version} applied successfully.",
                DeviceIdPersistenceSchemaVersion);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task BuildDeviceIdMigrationMapAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE TEMP TABLE _device_id_migration_map (
                legacy_vehicle_id text PRIMARY KEY,
                device_id uuid NOT NULL,
                vehicle_name varchar(100) NOT NULL
            ) ON COMMIT DROP;
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            INSERT INTO _device_id_migration_map (legacy_vehicle_id, device_id, vehicle_name)
            SELECT
                legacy,
                CASE
                    WHEN EXISTS (
                        SELECT 1 FROM fleet_devices d WHERE d.vehicle_name = legacy
                    )
                        THEN (
                            SELECT d.device_id
                            FROM fleet_devices d
                            WHERE d.vehicle_name = legacy
                            LIMIT 1
                        )
                    WHEN legacy ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                        THEN legacy::uuid
                    ELSE gen_random_uuid()
                END,
                CASE
                    WHEN char_length(legacy) < 2 THEN left(legacy || '-legacy', 100)
                    ELSE left(legacy, 100)
                END
            FROM (
                SELECT DISTINCT "VehicleId" AS legacy FROM telemetry_events
                WHERE "VehicleId" IS NOT NULL AND btrim("VehicleId") <> ''
                UNION
                SELECT DISTINCT "VehicleId" FROM fleet_vehicle_state
                WHERE "VehicleId" IS NOT NULL AND btrim("VehicleId") <> ''
                UNION
                SELECT DISTINCT "VehicleId" FROM fleet_alerts
                WHERE "VehicleId" IS NOT NULL AND btrim("VehicleId") <> ''
                UNION
                SELECT DISTINCT "VehicleId" FROM fleet_alert_states
                WHERE "VehicleId" IS NOT NULL AND btrim("VehicleId") <> ''
                UNION
                SELECT DISTINCT "VehicleId" FROM fleet_offline_publish_markers
                WHERE "VehicleId" IS NOT NULL AND btrim("VehicleId") <> ''
            ) identities;
            """,
            cancellationToken);
    }

    private static async Task InsertMigratedDevicesAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        // Primero los que no chocan por nombre; luego reintentos con sufijo ante UNIQUE(vehicle_name).
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            DO $$
            DECLARE
                r RECORD;
                candidate text;
                suffix int;
            BEGIN
                FOR r IN
                    SELECT legacy_vehicle_id, device_id, vehicle_name
                    FROM _device_id_migration_map
                    ORDER BY legacy_vehicle_id
                LOOP
                    IF EXISTS (SELECT 1 FROM fleet_devices d WHERE d.device_id = r.device_id) THEN
                        CONTINUE;
                    END IF;

                    candidate := r.vehicle_name;
                    suffix := 0;

                    LOOP
                        BEGIN
                            INSERT INTO fleet_devices (device_id, vehicle_name, created_at, updated_at)
                            VALUES (r.device_id, candidate, NOW(), NOW());
                            EXIT;
                        EXCEPTION WHEN unique_violation THEN
                            IF EXISTS (SELECT 1 FROM fleet_devices d WHERE d.device_id = r.device_id) THEN
                                EXIT;
                            END IF;
                            suffix := suffix + 1;
                            candidate := left(r.vehicle_name, greatest(1, 100 - length('-' || suffix::text)))
                                || '-' || suffix::text;
                        END;
                    END LOOP;
                END LOOP;
            END $$;
            """,
            cancellationToken);
    }

    // Alinea la secuencia VH-###: si el máximo es M, el próximo nextval es M+1; si no hay nombres, el próximo es 1.
    private static async Task SyncFleetVehicleNameSequenceAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            SELECT setval(
                'fleet_vehicle_name_seq',
                COALESCE((
                    SELECT MAX(substring(vehicle_name FROM '^VH-([0-9]+)$')::bigint)
                    FROM fleet_devices
                    WHERE vehicle_name ~ '^VH-[0-9]+$'
                ), 0),
                true
            );
            """,
            cancellationToken);
    }

    private static async Task ConvertOperationalTableToDeviceIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        ILogger logger,
        string tableName,
        string[]? primaryKeyColumns,
        string? primaryKeyName,
        string[]? dropIndexes,
        string? createIndexesSql,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, tableName, cancellationToken))
            return;

        if (await ColumnExistsAsync(connection, transaction, tableName, "device_id", cancellationToken)
            && !await ColumnExistsAsync(connection, transaction, tableName, "VehicleId", cancellationToken))
            return;

        await ExecuteSqlAsync(
            connection,
            transaction,
            $"ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS device_id UUID;",
            cancellationToken);

        if (await ColumnExistsAsync(connection, transaction, tableName, "VehicleId", cancellationToken))
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                $"""
                UPDATE {tableName} t
                SET device_id = m.device_id
                FROM _device_id_migration_map m
                WHERE t."VehicleId" = m.legacy_vehicle_id
                  AND t.device_id IS NULL;
                """,
                cancellationToken);

            await PreserveUnmappedOperationalRowsAsync(
                connection,
                transaction,
                logger,
                tableName,
                cancellationToken);
        }

        if (dropIndexes is not null)
        {
            foreach (var indexName in dropIndexes)
            {
                await ExecuteSqlAsync(
                    connection,
                    transaction,
                    $"DROP INDEX IF EXISTS {indexName};",
                    cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(primaryKeyName))
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                $"""
                ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS "{primaryKeyName}";
                """,
                cancellationToken);
        }

        if (await ColumnExistsAsync(connection, transaction, tableName, "VehicleId", cancellationToken))
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                $"""
                ALTER TABLE {tableName} DROP COLUMN "VehicleId";
                """,
                cancellationToken);
        }

        await ExecuteSqlAsync(
            connection,
            transaction,
            $"""
            ALTER TABLE {tableName} ALTER COLUMN device_id SET NOT NULL;
            """,
            cancellationToken);

        if (primaryKeyColumns is { Length: > 0 } && !string.IsNullOrWhiteSpace(primaryKeyName))
        {
            var pkCols = string.Join(", ", primaryKeyColumns);
            await ExecuteSqlAsync(
                connection,
                transaction,
                $"""
                ALTER TABLE {tableName} ADD CONSTRAINT "{primaryKeyName}" PRIMARY KEY ({pkCols});
                """,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(createIndexesSql))
            await ExecuteSqlAsync(connection, transaction, createIndexesSql, cancellationToken);
    }

    // Conserva filas históricas sin mapeo: asigna DeviceId, registra en fleet_devices y evita DELETE silencioso.
    private static async Task PreserveUnmappedOperationalRowsAsync(
        DbConnection connection,
        DbTransaction transaction,
        ILogger logger,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = $"""
            SELECT COUNT(*)::bigint
            FROM {tableName}
            WHERE device_id IS NULL;
            """;
        var unmappedCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (unmappedCount == 0)
            return;

        logger.LogWarning(
            "Migración DeviceId: {UnmappedCount} filas en {TableName} sin mapeo; se generan device_id y se registran en fleet_devices.",
            unmappedCount,
            tableName);

        await ExecuteSqlAsync(
            connection,
            transaction,
            $"""
            DO $$
            DECLARE
                r RECORD;
                assigned uuid;
                candidate text;
                suffix int;
            BEGIN
                FOR r IN
                    SELECT DISTINCT "VehicleId" AS legacy
                    FROM {tableName}
                    WHERE device_id IS NULL
                      AND "VehicleId" IS NOT NULL
                      AND btrim("VehicleId") <> ''
                    ORDER BY 1
                LOOP
                    SELECT d.device_id INTO assigned
                    FROM fleet_devices d
                    WHERE d.vehicle_name = r.legacy
                    LIMIT 1;

                    IF assigned IS NULL
                       AND r.legacy ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
                        assigned := r.legacy::uuid;
                    END IF;

                    IF assigned IS NULL THEN
                        assigned := gen_random_uuid();
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM fleet_devices d WHERE d.device_id = assigned) THEN
                        candidate := CASE
                            WHEN char_length(r.legacy) < 2 THEN left(r.legacy || '-legacy', 100)
                            ELSE left(r.legacy, 100)
                        END;
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
                                candidate := left(
                                    CASE
                                        WHEN char_length(r.legacy) < 2 THEN left(r.legacy || '-legacy', 100)
                                        ELSE left(r.legacy, 100)
                                    END,
                                    greatest(1, 100 - length('-' || suffix::text)))
                                    || '-' || suffix::text;
                            END;
                        END LOOP;
                    END IF;

                    UPDATE {tableName}
                    SET device_id = assigned
                    WHERE device_id IS NULL
                      AND "VehicleId" = r.legacy;
                END LOOP;

                -- Filas con VehicleId nulo/vacío: conservar con UUID nuevo anónimo.
                FOR r IN
                    SELECT ctid AS row_ctid
                    FROM {tableName}
                    WHERE device_id IS NULL
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

                    UPDATE {tableName}
                    SET device_id = assigned
                    WHERE ctid = r.row_ctid;
                END LOOP;
            END $$;
            """,
            cancellationToken);
    }

    private static async Task ConvertTelemetryEventsToDeviceIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "telemetry_events", cancellationToken))
            return;

        if (await ContinuousAggregateExistsAsync(connection, transaction, "telemetry_hourly", cancellationToken))
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                SELECT remove_continuous_aggregate_policy('telemetry_hourly', if_exists => TRUE);
                """,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                DROP MATERIALIZED VIEW IF EXISTS telemetry_hourly CASCADE;
                """,
                cancellationToken);
        }

        // Necesario para poder alterar segmentby / columnas en hypertables con compresión.
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            DO $$
            BEGIN
                BEGIN
                    PERFORM remove_compression_policy('telemetry_events', if_exists => TRUE);
                EXCEPTION WHEN OTHERS THEN
                    NULL;
                END;

                BEGIN
                    ALTER TABLE telemetry_events SET (timescaledb.compress = false);
                EXCEPTION WHEN OTHERS THEN
                    NULL;
                END;
            END $$;
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            ALTER TABLE telemetry_events ADD COLUMN IF NOT EXISTS device_id UUID;
            """,
            cancellationToken);

        if (await ColumnExistsAsync(connection, transaction, "telemetry_events", "VehicleId", cancellationToken))
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                UPDATE telemetry_events t
                SET device_id = m.device_id
                FROM _device_id_migration_map m
                WHERE t."VehicleId" = m.legacy_vehicle_id
                  AND t.device_id IS NULL;
                """,
                cancellationToken);

            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                UPDATE telemetry_events
                SET device_id = "VehicleId"::uuid
                WHERE device_id IS NULL
                  AND "VehicleId" ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';
                """,
                cancellationToken);

            await PreserveUnmappedOperationalRowsAsync(
                connection,
                transaction,
                logger,
                "telemetry_events",
                cancellationToken);
        }

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS ix_telemetry_events_device_timestamp_event
            ON telemetry_events (device_id, "Timestamp" DESC, "EventId" DESC);
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            DROP INDEX IF EXISTS ix_telemetry_events_vehicle_timestamp;
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            DROP INDEX IF EXISTS ix_telemetry_events_vehicle_timestamp_event;
            """,
            cancellationToken);

        // SAVEPOINT implícito vía bloque EXCEPTION: evita abortar la transacción si Timescale bloquea DROP.
        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'telemetry_events'
                      AND column_name = 'VehicleId'
                ) THEN
                    BEGIN
                        ALTER TABLE telemetry_events DROP COLUMN "VehicleId";
                    EXCEPTION WHEN OTHERS THEN
                        ALTER TABLE telemetry_events ALTER COLUMN "VehicleId" DROP NOT NULL;
                        COMMENT ON COLUMN telemetry_events."VehicleId" IS
                            'LEGACY unused after v7; identity is device_id. Kept only if Timescale blocked DROP.';
                    END;
                END IF;
            END $$;
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            ALTER TABLE telemetry_events ALTER COLUMN device_id SET NOT NULL;
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            ALTER TABLE telemetry_events SET (
                timescaledb.compress,
                timescaledb.compress_segmentby = 'device_id',
                timescaledb.compress_orderby = '"Timestamp" DESC'
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            SELECT add_compression_policy(
                'telemetry_events',
                compress_after => INTERVAL '7 days',
                if_not_exists => TRUE);
            """,
            cancellationToken);
    }

    private static async Task RecreateTelemetryHourlyAggregateAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await ContinuousAggregateExistsAsync(connection, transaction, "telemetry_hourly", cancellationToken))
            return;

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE MATERIALIZED VIEW telemetry_hourly
            WITH (timescaledb.continuous) AS
            SELECT
                time_bucket(INTERVAL '1 hour', "Timestamp") AS "Bucket",
                device_id,
                COUNT(*)::bigint AS "SampleCount",
                AVG("SpeedKmh") AS "AverageSpeedKmh",
                MAX("SpeedKmh") AS "MaxSpeedKmh",
                MIN("FuelLevelPercent") AS "MinFuelLevelPercent",
                MIN("BatteryPercent") AS "MinBatteryPercent"
            FROM telemetry_events
            GROUP BY 1, 2
            WITH NO DATA;
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            SELECT add_continuous_aggregate_policy(
                'telemetry_hourly',
                start_offset => INTERVAL '30 days',
                end_offset => INTERVAL '10 minutes',
                schedule_interval => INTERVAL '15 minutes',
                if_not_exists => TRUE);
            """,
            cancellationToken);
    }

    private static async Task EnsureFleetOfflinePublishMarkersSchemaAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var tableExists = await TableExistsAsync(connection, transaction, "fleet_offline_publish_markers", cancellationToken);
        if (!tableExists)
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                CREATE TABLE fleet_offline_publish_markers (
                    device_id UUID NOT NULL,
                    "LastEventId" uuid NOT NULL,
                    "StatusEvaluatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_fleet_offline_publish_markers" PRIMARY KEY (device_id)
                );
                """,
                cancellationToken);
            return;
        }

        if (!await ColumnExistsAsync(connection, transaction, "fleet_offline_publish_markers", "device_id", cancellationToken)
            && await ColumnExistsAsync(connection, transaction, "fleet_offline_publish_markers", "VehicleId", cancellationToken))
        {
            // v7 aún no aplicada en este camino; el DDL base deja VehicleId.
            return;
        }
    }

    private static async Task EnsureTelemetryEventsDeviceIdSchemaAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(connection, transaction, "telemetry_events", "device_id", cancellationToken))
            return;

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS ix_telemetry_events_device_timestamp_event
            ON telemetry_events (device_id, "Timestamp" DESC, "EventId" DESC);
            """,
            cancellationToken);
    }

    private static async Task EnsureFleetAlertsDeviceIdSchemaAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(connection, transaction, "fleet_alerts", "device_id", cancellationToken))
            return;

        await ExecuteSqlAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS ix_fleet_alerts_device_created
            ON fleet_alerts (device_id, "CreatedAt" DESC);
            """,
            cancellationToken);
    }

    private static async Task<bool> ContinuousAggregateExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string viewName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM timescaledb_information.continuous_aggregates
                WHERE view_schema = 'public'
                  AND view_name = @viewName
            );
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "viewName";
        parameter.Value = viewName;
        command.Parameters.Add(parameter);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ExecuteDeterministicBackfillAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var stateHasDeviceId = await ColumnExistsAsync(
            connection, transaction, "fleet_vehicle_state", "device_id", cancellationToken);
        var telemetryHasDeviceId = await ColumnExistsAsync(
            connection, transaction, "telemetry_events", "device_id", cancellationToken);

        if (stateHasDeviceId && telemetryHasDeviceId)
        {
            await ExecuteSqlAsync(
                connection,
                transaction,
                """
                INSERT INTO fleet_vehicle_state (
                    device_id, "LastEventId", "DriverId", "LastTimestamp", "Latitude", "Longitude",
                    "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "LocationSource", "UpdatedAt"
                )
                SELECT
                    latest.device_id,
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
                    SELECT DISTINCT ON (device_id)
                        "EventId", device_id, "DriverId", "Timestamp", "Latitude", "Longitude",
                        "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "LocationSource"
                    FROM telemetry_events
                    ORDER BY device_id, "Timestamp" DESC, "EventId" DESC
                ) AS latest
                ON CONFLICT (device_id) DO UPDATE
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
            return;
        }

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

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name = @tableName
            );
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @tableName
                  AND column_name = @columnName
            );
            """;
        var tableParam = command.CreateParameter();
        tableParam.ParameterName = "tableName";
        tableParam.Value = tableName;
        command.Parameters.Add(tableParam);

        var columnParam = command.CreateParameter();
        columnParam.ParameterName = "columnName";
        columnParam.Value = columnName;
        command.Parameters.Add(columnParam);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
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
