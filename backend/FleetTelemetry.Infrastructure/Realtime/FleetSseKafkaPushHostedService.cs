using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Consume fleet.realtime con consumer group por réplica y empuja offsets como StreamId SSE.
public sealed class FleetSseKafkaPushHostedService : BackgroundService
{
    private readonly FleetSseBroker _broker;
    private readonly KafkaOptions _kafkaOptions;
    private readonly SseOptions _sseOptions;
    private readonly ILogger<FleetSseKafkaPushHostedService> _logger;

    public FleetSseKafkaPushHostedService(
        FleetSseBroker broker,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions,
        ILogger<FleetSseKafkaPushHostedService> logger)
    {
        _broker = broker;
        _kafkaOptions = kafkaOptions.Value;
        _sseOptions = sseOptions.Value;
        _logger = logger;
    }

    internal string ConsumerGroupId =>
        $"{_kafkaOptions.RealtimeConsumerGroupBase}-{_sseOptions.InstanceId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RealtimeTopicValidator.EnsureSinglePartition(
            _kafkaOptions.BootstrapServers,
            _kafkaOptions.RealtimeTopic,
            _sseOptions.RequireSingleRealtimePartition);
        stoppingToken.ThrowIfCancellationRequested();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_kafkaOptions.RealtimeTopic);

        _logger.LogInformation(
            "SSE Kafka push consumer started. Topic={Topic} Group={Group} InstanceId={InstanceId}",
            _kafkaOptions.RealtimeTopic,
            ConsumerGroupId,
            _sseOptions.InstanceId);

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
                    var streamId = result.Offset.Value;

                    if (_broker.TryPublishExternal(
                            streamId,
                            message.EventType,
                            message.Payload,
                            message.OccurredAt))
                    {
                        consumer.Commit(result);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Skipped duplicate realtime event at offset {Offset}",
                            streamId);
                        consumer.Commit(result);
                    }
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
