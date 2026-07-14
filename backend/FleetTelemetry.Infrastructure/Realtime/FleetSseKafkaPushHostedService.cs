using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Consume fleet.realtime con Assign manual (sin Subscribe/rebalance) y empuja al broker SSE.
public sealed class FleetSseKafkaPushHostedService : BackgroundService
{
    private readonly FleetSseBroker _broker;
    private readonly KafkaOptions _kafkaOptions;
    private readonly SseOptions _sseOptions;
    private readonly IRealtimeStreamCoordinator _coordinator;
    private readonly ILogger<FleetSseKafkaPushHostedService> _logger;
    private readonly FleetTelemetryMetrics? _metrics;

    public FleetSseKafkaPushHostedService(
        FleetSseBroker broker,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions,
        IRealtimeStreamCoordinator coordinator,
        ILogger<FleetSseKafkaPushHostedService> logger,
        FleetTelemetryMetrics? metrics = null)
    {
        _broker = broker;
        _kafkaOptions = kafkaOptions.Value;
        _sseOptions = sseOptions.Value;
        _coordinator = coordinator;
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

            using var consumer = CreateConsumer();
            var topicPartition = new TopicPartition(_kafkaOptions.RealtimeTopic, 0);
            var watermarks = consumer.QueryWatermarkOffsets(topicPartition, TimeSpan.FromSeconds(10));
            var high = watermarks.High.Value;
            var baseline = high - 1;

            _broker.EstablishBaseline(baseline);
            consumer.Assign([new TopicPartitionOffset(topicPartition, new Offset(high))]);
            EnsureAssigned(consumer, topicPartition);

            _coordinator.EnterReady(baseline);
            _logger.LogInformation(
                "SSE Kafka push Ready via Assign. Topic={Topic} Group={Group} Baseline={Baseline} High={High} InstanceId={InstanceId}",
                _kafkaOptions.RealtimeTopic,
                ConsumerGroupId,
                baseline,
                high,
                _sseOptions.InstanceId);

            using var transport = new KafkaRealtimePushTransport(consumer);
            var processor = new RealtimeKafkaPushProcessor(_broker, _logger, _metrics);
            var loop = new FleetRealtimeKafkaPushLoop(processor, _logger);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_coordinator.State == RealtimeStreamState.Faulted)
                {
                    _logger.LogError(
                        "SSE Kafka push stopped: Faulted. Reason={Reason}",
                        _coordinator.FaultReason);
                    break;
                }

                try
                {
                    var pollResult = await loop.RunOnceAsync(
                        transport,
                        TimeSpan.FromMilliseconds(500),
                        stoppingToken);

                    switch (pollResult)
                    {
                        case KafkaPushPollResult.Idle:
                            break;

                        case KafkaPushPollResult.Completed:
                            if (_coordinator.State == RealtimeStreamState.Recovering)
                                _coordinator.EnterReady();
                            break;

                        case KafkaPushPollResult.TransientFailure:
                            if (_coordinator.State == RealtimeStreamState.Ready)
                                _coordinator.EnterRecovering("Transient publish failure");
                            break;

                        case KafkaPushPollResult.FatalFailure:
                            await RecoverConsumerAsync(consumer, topicPartition, loop, stoppingToken);
                            break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled Kafka realtime push loop error");
                    if (_coordinator.State == RealtimeStreamState.Ready)
                        _coordinator.EnterRecovering(ex.Message);
                }
            }

            consumer.Close();
            _logger.LogInformation("SSE Kafka push consumer stopped");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _coordinator.EnterFaulted(ex.Message);
            _logger.LogError(ex, "SSE Kafka push consumer faulted during startup");
            throw;
        }

        await Task.CompletedTask;
    }

    private async Task RecoverConsumerAsync(
        IConsumer<string, string> consumer,
        TopicPartition topicPartition,
        FleetRealtimeKafkaPushLoop loop,
        CancellationToken cancellationToken)
    {
        if (_coordinator.State != RealtimeStreamState.Faulted)
            _coordinator.EnterRecovering("Fatal Kafka transport failure");

        loop.AbandonPending();

        try
        {
            var watermarks = consumer.QueryWatermarkOffsets(topicPartition, TimeSpan.FromSeconds(10));
            var resumeOffset = _broker.LastProcessedExternalOffset + 1;

            if (resumeOffset < watermarks.Low.Value)
            {
                var baseline = watermarks.High.Value - 1;
                _broker.ResetToBaseline(baseline);
                consumer.Assign([new TopicPartitionOffset(topicPartition, watermarks.High)]);
                EnsureAssigned(consumer, topicPartition);
                _coordinator.EnterReady(baseline);
                _logger.LogWarning(
                    "SSE Kafka push retention gap. NewBaseline={Baseline} High={High}",
                    baseline,
                    watermarks.High.Value);
            }
            else
            {
                consumer.Assign([new TopicPartitionOffset(topicPartition, new Offset(resumeOffset))]);
                EnsureAssigned(consumer, topicPartition);
                _coordinator.EnterReady();
                _logger.LogInformation(
                    "SSE Kafka push recovered. ResumeOffset={ResumeOffset}",
                    resumeOffset);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _coordinator.EnterFaulted($"Recovery failed: {ex.Message}");
            _logger.LogError(ex, "SSE Kafka push recovery failed");
        }
    }

    private IConsumer<string, string> CreateConsumer()
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = ConsumerGroupId,
            EnableAutoCommit = false,
            // Assign manual: no se usa AutoOffsetReset ni Subscribe.
            AutoOffsetReset = AutoOffsetReset.Latest
        };

        return new ConsumerBuilder<string, string>(consumerConfig).Build();
    }

    private static void EnsureAssigned(
        IConsumer<string, string> consumer,
        TopicPartition topicPartition)
    {
        var assigned = consumer.Assignment;
        if (assigned is null
            || assigned.Count != 1
            || assigned[0].Topic != topicPartition.Topic
            || assigned[0].Partition != topicPartition.Partition)
        {
            throw new InvalidOperationException(
                $"Assign manual falló para {topicPartition.Topic}/{topicPartition.Partition.Value}.");
        }
    }
}
