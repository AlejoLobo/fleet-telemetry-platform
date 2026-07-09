using FleetTelemetry.Domain.Entities;

// Contrato de publicación de eventos en cola.
namespace FleetTelemetry.Application.Interfaces;

// Envía telemetría a Kafka para procesamiento asíncrono.
public interface ITelemetryEventPublisher
{
    // Publica un evento individual.
    Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
    // Publica un lote de eventos.
    Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default);
}
