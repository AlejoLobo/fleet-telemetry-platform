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

// Persiste evento, alertas e idempotencia en una transacción.
public class TimescaleTelemetryProcessingUnitOfWork : ITelemetryProcessingUnitOfWork
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FleetDbContext _dbContext;
    private readonly IFleetRealtimePublisher _realtimePublisher;
    private readonly TimeProvider _timeProvider;
    private readonly QueryLimitsOptions _queryLimits;
    private readonly ILogger<TimescaleTelemetryProcessingUnitOfWork> _logger;

    public TimescaleTelemetryProcessingUnitOfWork(
        FleetDbContext dbContext,
        IFleetRealtimePublisher realtimePublisher,
        TimeProvider timeProvider,
        IOptions<QueryLimitsOptions> queryLimits,
        ILogger<TimescaleTelemetryProcessingUnitOfWork> logger)
    {
        _dbContext = dbContext;
        _realtimePublisher = realtimePublisher;
        _timeProvider = timeProvider;
        _queryLimits = queryLimits.Value;
        _logger = logger;
    }

    // Procesa evento en transacción con idempotencia y alertas.
    public async Task<ProcessTelemetryOutcome> ProcessAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var processedAt = DateTimeOffset.UtcNow;
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

        _dbContext.TelemetryEvents.Add(new TelemetryEventRecord
        {
            EventId = telemetryEvent.EventId,
            VehicleId = telemetryEvent.VehicleId,
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

        var alerts = TelemetryAlertEvaluator.Evaluate(telemetryEvent);
        foreach (var alert in alerts)
        {
            _dbContext.FleetAlerts.Add(new FleetAlertRecord
            {
                AlertId = alert.AlertId,
                VehicleId = alert.VehicleId,
                AlertType = alert.AlertType,
                Severity = alert.Severity,
                Message = alert.Message,
                CreatedAt = alert.CreatedAt,
                IsAcknowledged = alert.IsAcknowledged
            });

            _logger.LogInformation(
                "Alert generated: {AlertType} ({Severity}) for vehicle {VehicleId}",
                alert.AlertType,
                alert.Severity,
                alert.VehicleId);
        }

        var stateRowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO fleet_vehicle_state (
                "VehicleId", "LastEventId", "DriverId", "LastTimestamp", "Latitude", "Longitude",
                "SpeedKmh", "FuelLevelPercent", "BatteryPercent", "LocationSource", "UpdatedAt"
            )
            VALUES (
                {telemetryEvent.VehicleId},
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

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await PublishRealtimeUpdatesAsync(
            telemetryEvent,
            alerts,
            stateRowsAffected > 0,
            cancellationToken);

        return ProcessTelemetryOutcome.Processed;
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

                var vehicleUpdate = new VehicleLatestStatusResponse(
                    telemetryEvent.VehicleId,
                    telemetryEvent.VehicleId,
                    connectivityStatus,
                    telemetryEvent.Timestamp,
                    telemetryEvent.SpeedKmh,
                    telemetryEvent.Latitude,
                    telemetryEvent.Longitude,
                    null,
                    telemetryEvent.LocationSource);

                await _realtimePublisher.PublishVehicleUpdateAsync(
                    telemetryEvent.VehicleId,
                    JsonSerializer.Serialize(vehicleUpdate, JsonOptions),
                    cancellationToken);
            }

            foreach (var alert in alerts)
            {
                var alertDto = new FleetAlertResponse(
                    alert.AlertId,
                    alert.VehicleId,
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
            _logger.LogWarning(ex, "Realtime publish failed for vehicle {VehicleId}", telemetryEvent.VehicleId);
        }
    }
}
