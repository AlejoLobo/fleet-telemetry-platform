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
    private readonly FleetKafkaPushAssignmentCoordinator _assignment;

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
        _assignment = new FleetKafkaPushAssignmentCoordinator(broker, readiness, logger);
    }

    // Tests: misma estrategia de asignación que producción.
    internal FleetKafkaPushAssignmentCoordinator AssignmentCoordinator => _assignment;

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
                .SetPartitionsAssignedHandler((c, partitions) =>
                    _assignment.HandlePartitionsAssigned(c, partitions))
                .SetPartitionsRevokedHandler((_, partitions) =>
                    _assignment.HandlePartitionsRevoked(partitions))
                .SetPartitionsLostHandler((_, partitions) =>
                    _assignment.HandlePartitionsLost(partitions))
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
                    if (_readiness.State == FleetKafkaPushReadinessState.Faulted)
                    {
                        _logger.LogError(
                            "SSE Kafka push consumer stopped: Faulted. Reason={Reason}",
                            _readiness.FaultReason);
                        break;
                    }

                    try
                    {
                        var pollResult = await loop.RunOnceAsync(
                            transport,
                            TimeSpan.FromMilliseconds(500),
                            stoppingToken);

                        if (pollResult == KafkaPushPollResult.Successful)
                        {
                            _assignment.NotifySuccessfulPollCycle();
                        }
                        else if (pollResult == KafkaPushPollResult.FatalFailure)
                        {
                            _assignment.EnterFaulted("Fatal Kafka consume error");
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
            _assignment.EnterFaulted(ex.Message);
            _logger.LogError(ex, "SSE Kafka push consumer faulted during startup");
            throw;
        }

        await Task.CompletedTask;
    }

    // Delega a la estrategia compartida (tests / diagnóstico).
    internal IEnumerable<TopicPartitionOffset> OnPartitionsAssigned(
        IConsumer<string, string> consumer,
        IReadOnlyList<TopicPartition> partitions) =>
        _assignment.HandlePartitionsAssigned(consumer, partitions);
}
