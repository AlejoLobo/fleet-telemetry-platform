using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Realtime;

// Ciclo productivo Assign → poll → recrear consumidor ante FatalFailure.
internal sealed class KafkaManualAssignmentPump
{
    private readonly FleetSseBroker _broker;
    private readonly IRealtimeStreamCoordinator _coordinator;
    private readonly IRealtimeKafkaConsumerFactory _consumerFactory;
    private readonly string _topic;
    private readonly string _groupId;
    private readonly ILogger? _logger;
    private readonly FleetTelemetryMetrics? _metrics;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;
    private readonly TimeSpan _pollTimeout;
    private readonly TimeSpan _watermarkTimeout;
    private readonly bool _startInRecovery;

    public KafkaManualAssignmentPump(
        FleetSseBroker broker,
        IRealtimeStreamCoordinator coordinator,
        IRealtimeKafkaConsumerFactory consumerFactory,
        string topic,
        string groupId,
        ILogger? logger = null,
        FleetTelemetryMetrics? metrics = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? pollTimeout = null,
        TimeSpan? watermarkTimeout = null,
        bool startInRecovery = false)
    {
        _broker = broker;
        _coordinator = coordinator;
        _consumerFactory = consumerFactory;
        _topic = topic;
        _groupId = groupId;
        _logger = logger;
        _metrics = metrics;
        _delayAsync = delayAsync;
        _pollTimeout = pollTimeout ?? TimeSpan.FromMilliseconds(500);
        _watermarkTimeout = watermarkTimeout ?? TimeSpan.FromSeconds(10);
        _startInRecovery = startInRecovery;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Evita bloquear al llamador con QueryWatermarkOffsets/Assign síncronos.
        await Task.Yield();

        var topicPartition = new TopicPartition(_topic, 0);
        var isFirstSession = !_startInRecovery;

        while (!cancellationToken.IsCancellationRequested
               && _coordinator.State != RealtimeStreamState.Faulted)
        {
            IRealtimeKafkaConsumerSession? session = null;
            try
            {
                session = _consumerFactory.Create(_groupId);
                long? baselineForReady = PrepareAssignment(session, topicPartition, isFirstSession);
                isFirstSession = false;

                var processor = new RealtimeKafkaPushProcessor(_broker, _logger, _metrics);
                var loop = new FleetRealtimeKafkaPushLoop(
                    processor,
                    _logger,
                    delayAsync: _delayAsync);

                var fatal = await RunSessionAsync(session, loop, baselineForReady, cancellationToken);
                if (!fatal)
                    break;

                // FatalFailure: EnterRecovering ya aplicado; se dispone la sesión y se crea otra.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Kafka manual assignment pump session failed");
                if (_coordinator.State == RealtimeStreamState.Starting)
                {
                    _coordinator.EnterFaulted(ex.Message);
                    throw;
                }

                if (_coordinator.State != RealtimeStreamState.Faulted)
                    _coordinator.EnterRecovering(ex.Message);
            }
            finally
            {
                session?.Dispose();
            }
        }
    }

    private long? PrepareAssignment(
        IRealtimeKafkaConsumerSession session,
        TopicPartition topicPartition,
        bool isFirstSession)
    {
        var watermarks = session.QueryWatermarkOffsets(topicPartition, _watermarkTimeout);
        var low = watermarks.Low.Value;
        var high = watermarks.High.Value;

        if (isFirstSession)
        {
            var baseline = high - 1;
            _broker.EstablishBaseline(baseline);
            session.Assign([new TopicPartitionOffset(topicPartition, new Offset(high))]);
            _logger?.LogInformation(
                "SSE Kafka push first Assign. Topic={Topic} Baseline={Baseline} High={High}",
                _topic,
                baseline,
                high);
            return baseline;
        }

        var plan = KafkaResumePosition.Resolve(_broker.LastProcessedExternalOffset, low, high);
        if (plan.NewBaseline.HasValue)
        {
            _broker.ResetToBaseline(plan.NewBaseline.Value);
            _logger?.LogWarning(
                "SSE Kafka push discontinuity. Resume outside [{Low},{High}]; NewBaseline={Baseline} Assign={Assign}",
                low,
                high,
                plan.NewBaseline.Value,
                plan.AssignOffset);
        }
        else
        {
            _logger?.LogInformation(
                "SSE Kafka push resume Assign. ResumeOffset={Resume}",
                plan.AssignOffset);
        }

        session.Assign([new TopicPartitionOffset(topicPartition, new Offset(plan.AssignOffset))]);
        return plan.NewBaseline;
    }

    // true = FatalFailure (recrear consumidor); false = fin normal / cancel / Faulted.
    private async Task<bool> RunSessionAsync(
        IRealtimeKafkaPushTransport transport,
        FleetRealtimeKafkaPushLoop loop,
        long? baselineForReady,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested
               && _coordinator.State != RealtimeStreamState.Faulted)
        {
            var pollResult = await loop.RunOnceAsync(transport, _pollTimeout, cancellationToken);
            switch (pollResult)
            {
                case KafkaPushPollResult.Idle:
                case KafkaPushPollResult.Completed:
                case KafkaPushPollResult.TransientFailure:
                case KafkaPushPollResult.FatalFailure:
                    var transition = KafkaPushPollStateMachine.Apply(
                        _coordinator,
                        pollResult,
                        baselineForReady);
                    baselineForReady = transition.BaselineForReady;
                    if (transition.RecreateConsumer)
                    {
                        loop.AbandonPending();
                        return true;
                    }

                    break;
            }
        }

        return false;
    }
}
