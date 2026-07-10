using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

// Contrato de publicación de mensajes fallidos hacia el tópico dead-letter.
public interface IDeadLetterPublisher
{
    Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default);
}
