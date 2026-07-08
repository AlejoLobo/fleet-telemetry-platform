using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.UseCases;

public class IngestTelemetryBatchUseCase
{
    private readonly ITelemetryEventPublisher _publisher;

    public IngestTelemetryBatchUseCase(ITelemetryEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task ExecuteAsync(TelemetryBatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Events is null || request.Events.Count == 0)
            throw new ArgumentException("At least one event is required.");

        var domainEvents = new List<TelemetryEvent>();

        foreach (var telemetryRequest in request.Events)
        {
            TelemetryEventValidator.Validate(telemetryRequest);
            domainEvents.Add(TelemetryEventValidator.MapToDomain(telemetryRequest));
        }

        await _publisher.PublishBatchAsync(domainEvents, cancellationToken);
    }
}
