using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
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
    private readonly FleetTelemetryMetrics? _metrics;

    public FleetSseKafkaPushHostedService(
        FleetSseBroker broker,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions,
        ILogger<FleetSseKafkaPushHostedService> logger,
        FleetTelemetryMetrics? metrics = null)
    {
        _broker = broker;
        _kafkaOptions = kafkaOptions.Value;
        _sseOptions = sseOptions.Value;
        _logger = logger;
        _metrics = metrics;
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

        using var transport = new KafkaRealtimePushTransport(consumer, _kafkaOptions.RealtimeTopic);
        var processor = new RealtimeKafkaPushProcessor(_broker, _logger, _metrics);
        var loop = new FleetRealtimeKafkaPushLoop(processor);

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
                    loop.RunOnce(transport, TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka realtime consume error. Reason={Reason}", ex.Error.Reason);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
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
