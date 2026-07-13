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
    private readonly IFleetKafkaPushReadiness _readiness;
    private readonly ILogger<FleetSseKafkaPushHostedService> _logger;
    private readonly FleetTelemetryMetrics? _metrics;

    public FleetSseKafkaPushHostedService(
        FleetSseBroker broker,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions,
        IFleetKafkaPushReadiness readiness,
        ILogger<FleetSseKafkaPushHostedService> logger,
        FleetTelemetryMetrics? metrics = null)
    {
        _broker = broker;
        _kafkaOptions = kafkaOptions.Value;
        _sseOptions = sseOptions.Value;
        _readiness = readiness;
        _logger = logger;
        _metrics = metrics;
    }

    internal string ConsumerGroupId =>
        $"{_kafkaOptions.RealtimeConsumerGroupBase}-{_sseOptions.InstanceId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
                .SetPartitionsAssignedHandler((c, partitions) => OnPartitionsAssigned(c, partitions))
                .Build();

            consumer.Subscribe(_kafkaOptions.RealtimeTopic);

            using var transport = new KafkaRealtimePushTransport(consumer, _kafkaOptions.RealtimeTopic);
            var processor = new RealtimeKafkaPushProcessor(_broker, _logger, _metrics);
            var loop = new FleetRealtimeKafkaPushLoop(processor, _logger, _metrics);

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
                        await loop.RunOnceAsync(transport, TimeSpan.FromMilliseconds(500), stoppingToken);
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Kafka realtime consume error. Reason={Reason}", ex.Error.Reason);
                    }
                    catch (KafkaException ex)
                    {
                        _logger.LogError(ex, "Kafka realtime transport error");
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled Kafka realtime push loop error");
                    }
                }
            }
            finally
            {
                consumer.Close();
                _logger.LogInformation("SSE Kafka push consumer stopped");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _readiness.MarkFaulted(ex.Message);
            _logger.LogError(ex, "SSE Kafka push consumer faulted during startup");
            throw;
        }

        await Task.CompletedTask;
    }

    // Asigna la partición única en High (Latest) y marca Ready solo con posición establecida.
    internal IEnumerable<TopicPartitionOffset> OnPartitionsAssigned(
        IConsumer<string, string> consumer,
        IReadOnlyList<TopicPartition> partitions)
    {
        _readiness.MarkAssigned();

        if (partitions.Count != 1)
        {
            var reason = $"Expected exactly one partition assignment, got {partitions.Count}.";
            _readiness.MarkFaulted(reason);
            throw new InvalidOperationException(reason);
        }

        var partition = partitions[0];
        var watermarks = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10));
        var nextOffset = watermarks.High.Value;
        _readiness.EstablishInitialPosition(nextOffset);
        _readiness.MarkReady();

        _logger.LogInformation(
            "SSE Kafka push Ready. Topic={Topic} Partition={Partition} NextOffset={NextOffset}",
            partition.Topic,
            partition.Partition.Value,
            nextOffset);

        return [new TopicPartitionOffset(partition, watermarks.High)];
    }
}
