using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Consume fleet.realtime y empuja eventos al broker SSE sin polling de alertas.
public sealed class FleetSseKafkaPushHostedService : BackgroundService
{
    private readonly FleetSseBroker _broker;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<FleetSseKafkaPushHostedService> _logger;

    public FleetSseKafkaPushHostedService(
        FleetSseBroker broker,
        IOptions<KafkaOptions> kafkaOptions,
        ILogger<FleetSseKafkaPushHostedService> logger)
    {
        _broker = broker;
        _kafkaOptions = kafkaOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.RealtimeConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_kafkaOptions.RealtimeTopic);

        _logger.LogInformation(
            "SSE Kafka push consumer started. Topic={Topic} Group={Group}",
            _kafkaOptions.RealtimeTopic,
            _kafkaOptions.RealtimeConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (result?.Message?.Value is null)
                        continue;

                    var message = FleetRealtimeKafkaMessage.Deserialize(result.Message.Value);
                    _broker.Publish(
                        message.EventType,
                        message.Payload,
                        message.OccurredAt);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka realtime consume error. Reason={Reason}", ex.Error.Reason);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid fleet realtime payload skipped");
                }
            }
        }
        finally
        {
            consumer.Close();
            _logger.LogInformation("SSE Kafka push consumer stopped");
        }

        await Task.CompletedTask;
    }
}
