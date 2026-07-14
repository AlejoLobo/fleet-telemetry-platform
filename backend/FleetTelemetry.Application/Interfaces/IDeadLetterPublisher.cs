using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IDeadLetterPublisher
{
    Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default);
}
