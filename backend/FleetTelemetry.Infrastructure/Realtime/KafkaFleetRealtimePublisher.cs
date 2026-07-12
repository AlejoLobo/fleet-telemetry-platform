using System.Text.Json;
using Confluent.Kafka;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Publica actualizaciones de flota y alertas al tópico fleet.realtime.
public sealed class KafkaFleetRealtimePublisher : IFleetRealtimePublisher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaFleetRealtimePublisher> _logger;

    public KafkaFleetRealtimePublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaFleetRealtimePublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = _options.ProducerMessageTimeoutMs
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public Task PublishVehicleUpdateAsync(string vehicleId, string payloadJson, CancellationToken cancellationToken = default) =>
        PublishAsync(FleetRealtimeEventTypes.VehicleUpdate, vehicleId, payloadJson, cancellationToken);

    public Task PublishAlertAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        PublishAsync(FleetRealtimeEventTypes.Alert, "alerts", payloadJson, cancellationToken);

    private async Task PublishAsync(
        string eventType,
        string key,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var message = new FleetRealtimeKafkaMessage
        {
            EventType = eventType,
            Payload = payloadDoc.RootElement.Clone(),
            OccurredAt = DateTimeOffset.UtcNow,
            VehicleId = eventType == FleetRealtimeEventTypes.VehicleUpdate ? key : null
        };

        var kafkaMessage = new Message<string, string>
        {
            Key = key,
            Value = FleetRealtimeKafkaMessage.Serialize(message)
        };

        await _producer.ProduceAsync(_options.RealtimeTopic, kafkaMessage, cancellationToken);

        _logger.LogDebug(
            "Realtime event published. EventType={EventType} Topic={Topic} Key={Key}",
            eventType,
            _options.RealtimeTopic,
            key);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(3));
        _producer.Dispose();
    }
}
