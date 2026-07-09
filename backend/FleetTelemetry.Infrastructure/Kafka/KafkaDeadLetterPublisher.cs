using System.Text.Json;
using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Infrastructure.Kafka;

// Publica mensajes fallidos en el tópico DLQ de Kafka.
public class KafkaDeadLetterPublisher : IKafkaDeadLetterPublisher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly ILogger<KafkaDeadLetterPublisher> _logger;

    public KafkaDeadLetterPublisher(
        IOptions<KafkaOptions> options,
        ResiliencePipelineFactory resilience,
        ILogger<KafkaDeadLetterPublisher> logger)
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

    public async Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var kafkaMessage = new Message<string, string>
        {
            Key = message.OriginalKey ?? "unknown",
            Value = json
        };

        try
        {
            await _resilience.KafkaPublishPipeline.ExecuteAsync(
                async token => await _producer.ProduceAsync(_options.DlqTopic, kafkaMessage, token),
                cancellationToken);

            _logger.LogWarning(
                "Mensaje enviado a DLQ topic={Topic} partition={Partition} offset={Offset} reason={Reason}",
                _options.DlqTopic,
                message.Partition,
                message.Offset,
                message.FailureReason);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "No se pudo publicar en DLQ; circuit breaker Kafka abierto");
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
