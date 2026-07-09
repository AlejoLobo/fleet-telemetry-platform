using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Application.UseCases;

public class ProcessTelemetryEventUseCase
{
    private readonly ITelemetryProcessingUnitOfWork _processingUnitOfWork;
    private readonly ILogger<ProcessTelemetryEventUseCase> _logger;

    public ProcessTelemetryEventUseCase(
        ITelemetryProcessingUnitOfWork processingUnitOfWork,
        ILogger<ProcessTelemetryEventUseCase> logger)
    {
        _processingUnitOfWork = processingUnitOfWork;
        _logger = logger;
    }

    public async Task<ProcessTelemetryOutcome> ExecuteAsync(
        TelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _processingUnitOfWork.ProcessAsync(telemetryEvent, cancellationToken);

        if (outcome == ProcessTelemetryOutcome.Duplicate)
        {
            _logger.LogInformation(
                "Duplicate telemetry event skipped: {EventId} for vehicle {VehicleId}",
                telemetryEvent.EventId,
                telemetryEvent.VehicleId);
        }

        return outcome;
    }
}
