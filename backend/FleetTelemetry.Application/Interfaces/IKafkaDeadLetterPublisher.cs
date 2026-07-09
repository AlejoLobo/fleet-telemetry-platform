using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

// Publica mensajes fallidos en el tópico dead-letter de Kafka.
public interface IKafkaDeadLetterPublisher
{
    Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default);
}
