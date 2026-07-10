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

// Publica mensajes fallidos en el tópico telemetry.dead-letter.
public class KafkaDeadLetterPublisher : IDeadLetterPublisher, IDisposable
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
            Key = $"{message.OriginalTopic}:{message.Partition}:{message.Offset}",
            Value = json
        };

        try
        {
            var result = await _resilience.KafkaPublishPipeline.ExecuteAsync(
                async token => await _producer.ProduceAsync(_options.DeadLetterTopic, kafkaMessage, token),
                cancellationToken);

            _logger.LogWarning(
                "Dead letter message published. DeadLetterTopic={DeadLetterTopic} OriginalTopic={OriginalTopic} Partition={Partition} Offset={Offset} Reason={Reason} DlqPartition={DlqPartition} DlqOffset={DlqOffset}",
                _options.DeadLetterTopic,
                message.OriginalTopic,
                message.Partition,
                message.Offset,
                message.Reason,
                result.Partition.Value,
                result.Offset.Value);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(
                ex,
                "Dead letter publish blocked by Kafka circuit breaker. OriginalTopic={OriginalTopic} Partition={Partition} Offset={Offset}",
                message.OriginalTopic,
                message.Partition,
                message.Offset);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
