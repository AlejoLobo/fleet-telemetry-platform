using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;

namespace FleetTelemetry.Application.UseCases;

public class IngestTelemetryEventUseCase
{
    private readonly ITelemetryEventPublisher _publisher;

    public IngestTelemetryEventUseCase(ITelemetryEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task ExecuteAsync(TelemetryEventRequest request, CancellationToken cancellationToken = default)
    {
        TelemetryEventValidator.Validate(request);

        var domainEvent = TelemetryEventValidator.MapToDomain(request);
        await _publisher.PublishAsync(domainEvent, cancellationToken);
    }
}
