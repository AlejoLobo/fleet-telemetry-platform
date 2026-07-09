using Confluent.Kafka;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Kafka;

public class KafkaTelemetryEventPublisher : ITelemetryEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaTelemetryEventPublisher> _logger;

    public KafkaTelemetryEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaTelemetryEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        var json = TelemetryEventJsonSerializer.Serialize(telemetryEvent);
        var message = new Message<string, string>
        {
            Key = telemetryEvent.VehicleId,
            Value = json
        };

        var result = await _producer.ProduceAsync(_options.TelemetryTopic, message, cancellationToken);

        _logger.LogInformation(
            "Published telemetry event {EventId} for vehicle {VehicleId} to {Topic} partition {Partition}",
            telemetryEvent.EventId,
            telemetryEvent.VehicleId,
            result.Topic,
            result.Partition.Value);
    }

    public async Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var telemetryEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PublishAsync(telemetryEvent, cancellationToken);
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
