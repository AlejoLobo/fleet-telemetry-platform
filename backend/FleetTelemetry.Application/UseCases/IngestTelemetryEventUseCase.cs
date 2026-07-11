using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.UseCases;

public class IngestTelemetryEventUseCase
{
    private readonly ITelemetryEventPublisher _publisher;
    private readonly TelemetryEventValidator _validator;

    public IngestTelemetryEventUseCase(
        ITelemetryEventPublisher publisher,
        TelemetryEventValidator validator)
    {
        _publisher = publisher;
        _validator = validator;
    }

    public async Task ExecuteAsync(TelemetryEventRequest request, CancellationToken cancellationToken = default)
    {
        _validator.Validate(request);
        var domainEvent = _validator.MapToDomain(request);
        await _publisher.PublishAsync(domainEvent, cancellationToken);
    }
}
