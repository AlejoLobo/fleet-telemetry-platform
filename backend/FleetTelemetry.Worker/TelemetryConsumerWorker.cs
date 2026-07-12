using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Worker;

// Consume Kafka y reintenta el mismo offset hasta resultado terminal (at-least-once).
public class TelemetryConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly TelemetryMessageCoordinator _coordinator;
    private readonly FleetTelemetryMetrics _metrics;
    private readonly ILogger<TelemetryConsumerWorker> _logger;

    public TelemetryConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        TelemetryMessageCoordinator coordinator,
        FleetTelemetryMetrics metrics,
        ILogger<TelemetryConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _coordinator = coordinator;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var initScope = _scopeFactory.CreateScope())
        {
            var environment = initScope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            var timescaleOptions = initScope.ServiceProvider.GetRequiredService<IOptions<TimescaleDbOptions>>().Value;

            if (DatabaseInitializationPolicy.ShouldRun(environment, timescaleOptions))
            {
                await DatabaseInitializer.InitializeAsync(
                    initScope.ServiceProvider,
                    useAdvisoryLock: true,
                    stoppingToken);
            }
            else
            {
                _logger.LogInformation(
                    "Omitiendo inicialización automática del esquema TimescaleDB (entorno distinto de Development y AllowAutoSchemaInitialization=false).");
            }
        }

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            MaxPollIntervalMs = _kafkaOptions.MaxPollIntervalMilliseconds,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_kafkaOptions.TelemetryTopic);

        _logger.LogInformation(
            "Telemetry consumer started. Topic={Topic} DeadLetterTopic={DeadLetterTopic} Group={Group} MaxProcessingAttempts={MaxProcessingAttempts} MaxDeadLetterPublishAttempts={MaxDeadLetterPublishAttempts} MaxPollIntervalMs={MaxPollIntervalMs}",
            _kafkaOptions.TelemetryTopic,
            _kafkaOptions.DeadLetterTopic,
            _kafkaOptions.ConsumerGroup,
            _kafkaOptions.MaxProcessingAttempts,
            _kafkaOptions.MaxDeadLetterPublishAttempts,
            _kafkaOptions.MaxPollIntervalMilliseconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumeResult;

                try
                {
                    consumeResult = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error. Reason={Reason}", ex.Error.Reason);
                    continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                // Solo omitir cuando no hay resultado o Message; Value null se normaliza a vacío → DLQ.
                if (consumeResult is null || consumeResult.Message is null)
                    continue;

                using var activity = FleetTelemetryActivitySources.Worker.StartActivity("kafka.consume");
                activity?.SetTag("messaging.system", "kafka");
                activity?.SetTag("messaging.destination.name", consumeResult.Topic);
                activity?.SetTag("messaging.kafka.partition", consumeResult.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", consumeResult.Offset.Value);

                _metrics.KafkaMessagesConsumed.Add(1);

                var message = new KafkaConsumedMessage(
                    Payload: consumeResult.Message.Value ?? string.Empty,
                    Topic: consumeResult.Topic,
                    Partition: consumeResult.Partition.Value,
                    Offset: consumeResult.Offset.Value,
                    Key: consumeResult.Message.Key);

                try
                {
                    var result = await _coordinator.ProcessUntilTerminalAsync(message, stoppingToken);
                    if (result == CoordinatorResult.Commit)
                        consumer.Commit(consumeResult);
                    else if (result == CoordinatorResult.StopWithoutCommit)
                        return;
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
            _logger.LogInformation("Telemetry consumer stopped.");
        }
    }
}
