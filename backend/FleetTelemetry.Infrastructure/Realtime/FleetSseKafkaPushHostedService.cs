using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Hosted service: valida partición y delega en KafkaManualAssignmentPump.
public sealed class FleetSseKafkaPushHostedService : BackgroundService
{
    private readonly FleetSseBroker _broker;
    private readonly KafkaOptions _kafkaOptions;
    private readonly SseOptions _sseOptions;
    private readonly IRealtimeStreamCoordinator _coordinator;
    private readonly ILogger<FleetSseKafkaPushHostedService> _logger;
    private readonly FleetTelemetryMetrics? _metrics;
    private readonly IRealtimeKafkaConsumerFactory? _consumerFactoryOverride;

    public FleetSseKafkaPushHostedService(
        FleetSseBroker broker,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions,
        IRealtimeStreamCoordinator coordinator,
        ILogger<FleetSseKafkaPushHostedService> logger,
        FleetTelemetryMetrics? metrics = null)
        : this(broker, kafkaOptions, sseOptions, coordinator, logger, metrics, consumerFactoryOverride: null)
    {
    }

    // Tests: inyectar fábrica falsa sin cambiar el camino productivo.
    internal FleetSseKafkaPushHostedService(
        FleetSseBroker broker,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions,
        IRealtimeStreamCoordinator coordinator,
        ILogger<FleetSseKafkaPushHostedService> logger,
        FleetTelemetryMetrics? metrics,
        IRealtimeKafkaConsumerFactory? consumerFactoryOverride)
    {
        _broker = broker;
        _kafkaOptions = kafkaOptions.Value;
        _sseOptions = sseOptions.Value;
        _coordinator = coordinator;
        _logger = logger;
        _metrics = metrics;
        _consumerFactoryOverride = consumerFactoryOverride;
    }

    internal string ConsumerGroupId =>
        $"{_kafkaOptions.RealtimeConsumerGroupBase}-{_sseOptions.InstanceId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            stoppingToken.ThrowIfCancellationRequested();

            var factory = _consumerFactoryOverride
                ?? new ConfluentRealtimeKafkaConsumerFactory(
                    _kafkaOptions.BootstrapServers,
                    _kafkaOptions.RealtimeTopic);

            // Metadata recuperable vive en el mismo ciclo de backoff del pump (Starting sin SSE).
            var metadataSource = new ConfluentRealtimeTopicMetadataSource(_kafkaOptions.BootstrapServers);

            var pump = new KafkaManualAssignmentPump(
                _broker,
                _coordinator,
                factory,
                _kafkaOptions.RealtimeTopic,
                ConsumerGroupId,
                _logger,
                _metrics,
                metadataSource: metadataSource,
                requireSinglePartition: _sseOptions.RequireSingleRealtimePartition);

            _logger.LogInformation(
                "SSE Kafka push pump starting. Topic={Topic} Group={Group} InstanceId={InstanceId}",
                _kafkaOptions.RealtimeTopic,
                ConsumerGroupId,
                _sseOptions.InstanceId);

            await pump.RunAsync(stoppingToken);

            _logger.LogInformation("SSE Kafka push pump stopped");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_coordinator.State != RealtimeStreamState.Faulted)
                _coordinator.EnterFaulted(ex.Message);
            _logger.LogError(ex, "SSE Kafka push consumer faulted during startup");
            throw;
        }
    }
}
