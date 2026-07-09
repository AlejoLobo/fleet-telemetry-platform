using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Application.UseCases;

public class ProcessTelemetryEventUseCase
{
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ITelemetryRepository _telemetryRepository;
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<ProcessTelemetryEventUseCase> _logger;

    public ProcessTelemetryEventUseCase(
        IIdempotencyStore idempotencyStore,
        ITelemetryRepository telemetryRepository,
        IAlertRepository alertRepository,
        ILogger<ProcessTelemetryEventUseCase> logger)
    {
        _idempotencyStore = idempotencyStore;
        _telemetryRepository = telemetryRepository;
        _alertRepository = alertRepository;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        if (!await _idempotencyStore.TryAcquireAsync(telemetryEvent.EventId, cancellationToken))
        {
            _logger.LogInformation(
                "Duplicate telemetry event skipped: {EventId} for vehicle {VehicleId}",
                telemetryEvent.EventId,
                telemetryEvent.VehicleId);
            return false;
        }

        await _telemetryRepository.SaveAsync(telemetryEvent, cancellationToken);

        var alerts = TelemetryAlertEvaluator.Evaluate(telemetryEvent);
        foreach (var alert in alerts)
        {
            await _alertRepository.SaveAsync(alert, cancellationToken);
            _logger.LogInformation(
                "Alert generated: {AlertType} ({Severity}) for vehicle {VehicleId}",
                alert.AlertType,
                alert.Severity,
                alert.VehicleId);
        }

        return true;
    }
}
