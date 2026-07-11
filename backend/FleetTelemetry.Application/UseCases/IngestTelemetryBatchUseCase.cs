using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.UseCases;

public class IngestTelemetryBatchUseCase
{
    private readonly ITelemetryEventPublisher _publisher;
    private readonly TelemetryEventValidator _validator;
    private readonly TelemetryIngestOptions _options;

    public IngestTelemetryBatchUseCase(
        ITelemetryEventPublisher publisher,
        TelemetryEventValidator validator,
        IOptions<TelemetryIngestOptions> options)
    {
        _publisher = publisher;
        _validator = validator;
        _options = options.Value;
    }

    public async Task ExecuteAsync(TelemetryBatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Events is null || request.Events.Count == 0)
            throw new ArgumentException("At least one event is required.");

        if (request.Events.Count > _options.MaxBatchSize)
            throw new ArgumentException($"Batch size cannot exceed {_options.MaxBatchSize} events.");

        var domainEvents = new List<TelemetryEvent>();
        foreach (var telemetryRequest in request.Events)
        {
            _validator.Validate(telemetryRequest);
            domainEvents.Add(_validator.MapToDomain(telemetryRequest));
        }

        await _publisher.PublishBatchAsync(domainEvents, cancellationToken);
    }
}
