using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

// Unidad de trabajo transaccional para procesar telemetría.
namespace FleetTelemetry.Infrastructure.Repositories;

// Persiste evento, estados de alerta, alertas emitidas e idempotencia en una transacción.
public class TimescaleTelemetryProcessingUnitOfWork : ITelemetryProcessingUnitOfWork
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FleetDbContext _dbContext;
    private readonly IFleetRealtimePublisher _realtimePublisher;
    private readonly IFleetOfflinePublishMarkerRepository _markerRepository;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly QueryLimitsOptions _queryLimits;
    private readonly AlertingOptions _alerting;
    private readonly ILogger<TimescaleTelemetryProcessingUnitOfWork> _logger;

    public TimescaleTelemetryProcessingUnitOfWork(
        FleetDbContext dbContext,
        IFleetRealtimePublisher realtimePublisher,
        IFleetOfflinePublishMarkerRepository markerRepository,
        IDeviceRegistry deviceRegistry,
        TimeProvider timeProvider,
        IOptions<QueryLimitsOptions> queryLimits,
        IOptions<AlertingOptions> alerting,
        ILogger<TimescaleTelemetryProcessingUnitOfWork> logger)
    {
        _dbContext = dbContext;
        _realtimePublisher = realtimePublisher;
        _markerRepository = markerRepository;
        _deviceRegistry = deviceRegistry;
        _timeProvider = timeProvider;
        _queryLimits = queryLimits.Value;
        _alerting = alerting.Value;
        _logger = logger;
    }

    // Procesa evento en transacción con idempotencia, estados de alerta y alertas emitidas.
    public async Task<ProcessTelemetryOutcome> ProcessAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var processedAt = _timeProvider.GetUtcNow();
        var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO processed_events ("EventId", "ProcessedAt")
            VALUES ({telemetryEvent.EventId}, {processedAt})
            ON CONFLICT ("EventId") DO NOTHING
            """,
            cancellationToken);

        if (rowsAffected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogDebug("EventId {EventId} ya procesado (idempotencia)", telemetryEvent.EventId);
            return ProcessTelemetryOutcome.Duplicate;
        }

        var deviceId = telemetryEvent.DeviceId;

        _dbContext.TelemetryEvents.Add(new TelemetryEventRecord
        {
            EventId = telemetryEvent.EventId,
            DeviceId = deviceId,
            DriverId = telemetryEvent.DriverId,
            Timestamp = telemetryEvent.Timestamp,
            Latitude = telemetryEvent.Latitude,
            Longitude = telemetryEvent.Longitude,
            SpeedKmh = telemetryEvent.SpeedKmh,
            FuelLevelPercent = telemetryEvent.FuelLevelPercent,
            BatteryPercent = telemetryEvent.BatteryPercent,
            LocationSource = telemetryEvent.LocationSource,
            CapturedAt = processedAt
        });

        var deviceIdKey = deviceId.ToString("D");
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({deviceIdKey}));",
            cancellationToken);

        var stateRowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO fleet_vehicle_state (
                device_id, "LastEventId", "DriverId", "LastTimestamp", "Latitude", "Longitude",
                "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "LocationSource", "UpdatedAt"
            )
            VALUES (
                {deviceId},
                {telemetryEvent.EventId},
                {telemetryEvent.DriverId},
                {telemetryEvent.Timestamp},
                {telemetryEvent.Latitude},
                {telemetryEvent.Longitude},
                {telemetryEvent.SpeedKmh},
                {telemetryEvent.FuelLevelPercent},
                {telemetryEvent.BatteryPercent},
                {telemetryEvent.LocationSource},
                {processedAt}
            )
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

        IReadOnlyList<FleetAlert> emittedAlerts = Array.Empty<FleetAlert>();
        if (stateRowsAffected > 0)
        {
            var locked = await LockAlertStatesAsync(deviceId, cancellationToken);
            var evaluation = TelemetryAlertEvaluator.Evaluate(
                telemetryEvent,
                locked.StatesByType,
                processedAt,
                _alerting);

            ApplyAlertStateUpserts(evaluation.StatesToUpsert, locked.TrackedByType);
            emittedAlerts = evaluation.EmittedAlerts;

            foreach (var alert in emittedAlerts)
            {
                _dbContext.FleetAlerts.Add(new FleetAlertRecord
                {
                    AlertId = alert.AlertId,
                    DeviceId = alert.DeviceId,
                    AlertType = alert.AlertType,
                    Severity = alert.Severity,
                    Message = alert.Message,
                    CreatedAt = alert.CreatedAt,
                    IsAcknowledged = alert.IsAcknowledged
                });

                _logger.LogInformation(
                    "Alert generated: {AlertType} ({Severity}) for device {DeviceId}",
                    alert.AlertType,
                    alert.Severity,
                    alert.DeviceId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await PublishRealtimeUpdatesAsync(
            telemetryEvent,
            emittedAlerts,
            publishVehicleUpdate: stateRowsAffected > 0,
            cancellationToken);

        return ProcessTelemetryOutcome.Processed;
    }

    // Bloqueo de filas existentes para serializar evaluaciones concurrentes del mismo dispositivo.
    private async Task<(
        Dictionary<string, FleetAlertConditionState> StatesByType,
        Dictionary<string, FleetAlertConditionStateRecord> TrackedByType)> LockAlertStatesAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var records = await _dbContext.FleetAlertStates
            .FromSqlInterpolated(
                $"""
                SELECT device_id, "AlertType", "IsActive", "LastConditionAt", "LastAlertAt", "UpdatedAt"
                FROM fleet_alert_states
                WHERE device_id = {deviceId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var tracked = records.ToDictionary(r => r.AlertType, StringComparer.Ordinal);
        var states = records.ToDictionary(
            r => r.AlertType,
            r => FleetAlertConditionState.FromPersistence(
                r.DeviceId,
                r.AlertType,
                r.IsActive,
                r.LastConditionAt,
                r.LastAlertAt,
                r.UpdatedAt),
            StringComparer.Ordinal);

        return (states, tracked);
    }

    private void ApplyAlertStateUpserts(
        IReadOnlyList<FleetAlertConditionState> statesToUpsert,
        Dictionary<string, FleetAlertConditionStateRecord> trackedByType)
    {
        foreach (var state in statesToUpsert)
        {
            if (trackedByType.TryGetValue(state.AlertType, out var existing))
            {
                existing.IsActive = state.IsActive;
                existing.LastConditionAt = state.LastConditionAt;
                existing.LastAlertAt = state.LastAlertAt;
                existing.UpdatedAt = state.UpdatedAt;
                continue;
            }

            _dbContext.FleetAlertStates.Add(new FleetAlertConditionStateRecord
            {
                DeviceId = state.DeviceId,
                AlertType = state.AlertType,
                IsActive = state.IsActive,
                LastConditionAt = state.LastConditionAt,
                LastAlertAt = state.LastAlertAt,
                UpdatedAt = state.UpdatedAt
            });
        }
    }

    private async Task PublishRealtimeUpdatesAsync(
        TelemetryEvent telemetryEvent,
        IReadOnlyList<FleetAlert> alerts,
        bool publishVehicleUpdate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (publishVehicleUpdate)
            {
                var now = _timeProvider.GetUtcNow();
                var connectivityStatus = VehicleConnectivityStatus.Resolve(
                    telemetryEvent.Timestamp,
                    now,
                    _queryLimits.OnlineThresholdMinutes);

                var deviceId = telemetryEvent.DeviceId;
                var device = await _deviceRegistry.GetDeviceAsync(deviceId, cancellationToken);
                var vehicleName = device?.VehicleName ?? deviceId.ToString("D");
                var vehicleType = device?.VehicleType ?? Domain.ValueObjects.VehicleType.Default.Value;

                var vehicleUpdate = new VehicleLatestStatusResponse(
                    deviceId,
                    vehicleName,
                    vehicleType,
                    connectivityStatus,
                    telemetryEvent.Timestamp,
                    telemetryEvent.SpeedKmh,
                    telemetryEvent.Latitude,
                    telemetryEvent.Longitude,
                    null,
                    telemetryEvent.LocationSource,
                    telemetryEvent.EventId,
                    now,
                    telemetryEvent.DriverId);

                await _realtimePublisher.PublishVehicleUpdateAsync(
                    deviceId,
                    JsonSerializer.Serialize(vehicleUpdate, JsonOptions),
                    cancellationToken);

                if (connectivityStatus == VehicleConnectivityStatus.Online)
                {
                    await _markerRepository.MarkOnlineAsync(deviceId, cancellationToken);
                }
                else
                {
                    await _markerRepository.MarkOfflinePublishedAsync(
                        deviceId,
                        telemetryEvent.EventId,
                        now,
                        cancellationToken);
                }
            }

            foreach (var alert in alerts)
            {
                var alertDto = new FleetAlertResponse(
                    alert.AlertId,
                    alert.DeviceId,
                    alert.AlertType,
                    alert.Severity,
                    alert.Message,
                    alert.CreatedAt,
                    alert.IsAcknowledged);

                await _realtimePublisher.PublishAlertAsync(
                    JsonSerializer.Serialize(alertDto, JsonOptions),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // El push realtime no debe revertir la transacción ya confirmada.
            _logger.LogWarning(ex, "Realtime publish failed for device {DeviceId}", telemetryEvent.DeviceId);
        }
    }
}
