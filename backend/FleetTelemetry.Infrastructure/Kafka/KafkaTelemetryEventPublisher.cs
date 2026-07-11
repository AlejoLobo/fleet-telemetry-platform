using Confluent.Kafka;
using FleetTelemetry.Application.Contracts;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Infrastructure.Kafka;

// Publicador de eventos de telemetría en Kafka con batching configurable.
public class KafkaTelemetryEventPublisher : ITelemetryEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaTelemetryEventPublisher> _logger;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly FleetTelemetryMetrics _metrics;

    public KafkaTelemetryEventPublisher(
        IOptions<KafkaOptions> options,
        ResiliencePipelineFactory resilience,
        FleetTelemetryMetrics metrics,
        ILogger<KafkaTelemetryEventPublisher> logger)
    {
        _options = options.Value;
        _resilience = resilience;
        _metrics = metrics;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = _options.ProducerMessageTimeoutMs,
            LingerMs = 5
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        await PublishCoreAsync(telemetryEvent, cancellationToken);
    }

    public async Task PublishBatchAsync(IEnumerable<TelemetryEvent> events, CancellationToken cancellationToken = default)
    {
        var batch = events.ToList();
        if (batch.Count == 0)
            return;

        var chunkSize = Math.Max(1, _options.PublishBatchSize);
        for (var offset = 0; offset < batch.Count; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = batch.Skip(offset).Take(chunkSize);
            var deliveryTasks = chunk.Select(evt => PublishCoreAsync(evt, cancellationToken));
            await Task.WhenAll(deliveryTasks);
        }
    }

    private async Task PublishCoreAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        var json = TelemetryEventJsonSerializer.Serialize(telemetryEvent, _options.UseEventEnvelope);
        var message = new Message<string, string>
        {
            Key = telemetryEvent.VehicleId,
            Value = json,
            Headers = BuildHeaders(telemetryEvent)
        };

        try
        {
            var result = await _resilience.KafkaPublishPipeline.ExecuteAsync(
                async token => await _producer.ProduceAsync(_options.TelemetryTopic, message, token),
                cancellationToken);

            _logger.LogInformation(
                "Published telemetry event {EventId} for vehicle {VehicleId} to {Topic} partition {Partition}",
                telemetryEvent.EventId,
                telemetryEvent.VehicleId,
                result.Topic,
                result.Partition.Value);

            _metrics.TelemetryIngestedTotal.Add(1);
        }
        catch (BrokenCircuitException ex)
        {
            _metrics.KafkaPublishFailuresTotal.Add(1);
            _logger.LogError(ex, "Kafka publish bloqueado: circuit breaker abierto");
            throw new DependencyCircuitOpenException(
                ResilienceDependency.Kafka.ToString(),
                retryAfter: TimeSpan.FromSeconds(30));
        }
    }

    private static Headers BuildHeaders(TelemetryEvent telemetryEvent)
    {
        var headers = new Headers
        {
            { "schema-version", BitConverter.GetBytes(TelemetryEventEnvelope.CurrentSchemaVersion) },
            { "event-id", telemetryEvent.EventId.ToByteArray() },
            { "correlation-id", telemetryEvent.EventId.ToByteArray() }
        };
        return headers;
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
