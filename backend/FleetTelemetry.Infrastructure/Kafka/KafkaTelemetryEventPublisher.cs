using Confluent.Kafka;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

// Publicador de eventos de telemetría en Kafka.
namespace FleetTelemetry.Infrastructure.Kafka;

// Envía eventos al tópico con resiliencia y idempotencia del productor.
public class KafkaTelemetryEventPublisher : ITelemetryEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaTelemetryEventPublisher> _logger;
    private readonly ResiliencePipelineFactory _resilience;

    public KafkaTelemetryEventPublisher(
        IOptions<KafkaOptions> options,
        ResiliencePipelineFactory resilience,
        ILogger<KafkaTelemetryEventPublisher> logger)
    {
        _options = options.Value;
        _resilience = resilience;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    // Serializa y publica un evento con clave por vehículo.
    public async Task PublishAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        var json = TelemetryEventJsonSerializer.Serialize(telemetryEvent);
        var message = new Message<string, string>
        {
            Key = telemetryEvent.VehicleId,
            Value = json
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
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Kafka publish bloqueado: circuit breaker abierto");
            throw new DependencyCircuitOpenException(
                ResilienceDependency.Kafka.ToString(),
                retryAfter: TimeSpan.FromSeconds(30));
        }
    }

    // Publica eventos secuencialmente reutilizando PublishAsync.
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
