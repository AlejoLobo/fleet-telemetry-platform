using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// Unidad de trabajo transaccional para procesar telemetría.
namespace FleetTelemetry.Infrastructure.Repositories;

// Persiste evento, alertas e idempotencia en una transacción.
public class TimescaleTelemetryProcessingUnitOfWork : ITelemetryProcessingUnitOfWork
{
    private readonly FleetDbContext _dbContext;
    private readonly ILogger<TimescaleTelemetryProcessingUnitOfWork> _logger;

    public TimescaleTelemetryProcessingUnitOfWork(
        FleetDbContext dbContext,
        ILogger<TimescaleTelemetryProcessingUnitOfWork> logger)
    {
        _dbContext = dbContext;
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

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ProcessTelemetryOutcome.Processed;
    }
}
