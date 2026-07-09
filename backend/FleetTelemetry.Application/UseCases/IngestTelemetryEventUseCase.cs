using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;

// Caso de uso de ingesta de un evento de telemetría.
namespace FleetTelemetry.Application.UseCases;

// Valida y publica un evento individual a Kafka.
public class IngestTelemetryEventUseCase
{
    private readonly ITelemetryEventPublisher _publisher;

    public IngestTelemetryEventUseCase(ITelemetryEventPublisher publisher)
    {
        _publisher = publisher;
    }

    // Valida, mapea al dominio y publica.
    public async Task ExecuteAsync(TelemetryEventRequest request, CancellationToken cancellationToken = default)
    {
        TelemetryEventValidator.Validate(request);

        var domainEvent = TelemetryEventValidator.MapToDomain(request);
        await _publisher.PublishAsync(domainEvent, cancellationToken);
    }
}
