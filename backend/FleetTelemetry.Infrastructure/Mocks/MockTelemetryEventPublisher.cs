using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Mocks;

public class MockTelemetryEventPublisher : ITelemetryEventPublisher
{
    private readonly ILogger<MockTelemetryEventPublisher> _logger;

    public MockTelemetryEventPublisher(ILogger<MockTelemetryEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        // Fase 2: reemplazar por publicación real en Kafka
        _logger.LogInformation(
            "[MOCK] Published telemetry event {EventId} for vehicle {VehicleId}",
            telemetryEvent.EventId,
            telemetryEvent.VehicleId);

        return Task.CompletedTask;
    }

    public Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default)
    {
        var eventList = events.ToList();

        _logger.LogInformation("[MOCK] Published telemetry batch with {Count} events", eventList.Count);

        foreach (var telemetryEvent in eventList)
        {
            _logger.LogInformation(
                "[MOCK] Published telemetry event {EventId} for vehicle {VehicleId}",
                telemetryEvent.EventId,
                telemetryEvent.VehicleId);
        }

        return Task.CompletedTask;
    }
}
