using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

public interface ITelemetryEventPublisher
{
    Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
    Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default);
}
