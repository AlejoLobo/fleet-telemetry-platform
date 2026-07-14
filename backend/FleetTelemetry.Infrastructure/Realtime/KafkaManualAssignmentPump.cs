using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Realtime;

// Ciclo productivo ValidateTopic → Assign → poll → recrear consumidor ante FatalFailure / fallos transitorios.
internal sealed class KafkaManualAssignmentPump
{
    private static readonly TimeSpan MaxSessionBackoff = TimeSpan.FromSeconds(5);

    private readonly FleetSseBroker _broker;
    private readonly IRealtimeStreamCoordinator _coordinator;
    private readonly IRealtimeKafkaConsumerFactory _consumerFactory;
    private readonly string _topic;
    private readonly string _groupId;
    private readonly ILogger? _logger;
    private readonly FleetTelemetryMetrics? _metrics;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _pollTimeout;
    private readonly TimeSpan _watermarkTimeout;
    private readonly bool _startInRecovery;
    private readonly IRealtimeTopicMetadataSource? _metadataSource;
    private readonly bool _requireSinglePartition;
    private readonly TimeSpan _metadataTimeout;

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
        bool startInRecovery = false,
        IRealtimeTopicMetadataSource? metadataSource = null,
        bool requireSinglePartition = false,
        TimeSpan? metadataTimeout = null)
    {
        _broker = broker;
        _coordinator = coordinator;
        _consumerFactory = consumerFactory;
        _topic = topic;
        _groupId = groupId;
        _logger = logger;
        _metrics = metrics;
        _delayAsync = delayAsync ?? ((delay, ct) => Task.Delay(delay, ct));
        _pollTimeout = pollTimeout ?? TimeSpan.FromMilliseconds(500);
        _watermarkTimeout = watermarkTimeout ?? TimeSpan.FromSeconds(10);
        _startInRecovery = startInRecovery;
        _metadataSource = metadataSource;
        _requireSinglePartition = requireSinglePartition;
        _metadataTimeout = metadataTimeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Evita bloquear al llamador con QueryWatermarkOffsets/Assign síncronos.
        await Task.Yield();

        var topicPartition = new TopicPartition(_topic, 0);
        var isFirstSession = !_startInRecovery;
        var consecutiveFailures = 0;
        var topicValidated = false;

        while (!cancellationToken.IsCancellationRequested
               && _coordinator.State != RealtimeStreamState.Faulted)
        {
            IRealtimeKafkaConsumerSession? session = null;
            var phase = "ValidateTopic";
            var recreateWithBackoff = false;
            try
            {
                if (_metadataSource is not null && _requireSinglePartition && !topicValidated)
                {
                    phase = "ValidateTopic";
                    RealtimeTopicValidator.EnsureSinglePartition(
                        _metadataSource,
                        _topic,
                        required: true,
                        timeout: _metadataTimeout);
                    topicValidated = true;
                }

                phase = "CreateConsumer";
                session = _consumerFactory.Create(_groupId);

                phase = "QueryWatermarks";
                long? baselineForReady = PrepareAssignment(session, topicPartition, isFirstSession, ref phase);
                isFirstSession = false;

                // No reinicia consecutiveFailures aquí: solo Idle/Completed cuentan como saludables.

                phase = "Poll";
                var processor = new RealtimeKafkaPushProcessor(_broker, _logger, _metrics);
                var loop = new FleetRealtimeKafkaPushLoop(
                    processor,
                    _logger,
                    delayAsync: _delayAsync);

                var sessionResult = await RunSessionAsync(
                    session,
                    loop,
                    baselineForReady,
                    consecutiveFailures,
                    cancellationToken);
                consecutiveFailures = sessionResult.ConsecutiveFailures;
                if (!sessionResult.RecreateConsumer)
                    break;

                // FatalFailure antes o después de poll saludable: racha previa + 1; backoff tras disponer.
                consecutiveFailures++;
                recreateWithBackoff = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                phase = KafkaPushErrorClassifier.ResolveFailedPhase(phase, ex);
                if (KafkaPushErrorClassifier.IsPermanent(ex))
                {
                    _logger?.LogError(
                        ex,
                        "SSE Kafka push permanent failure. Phase={Phase} Topic={Topic} Group={Group}",
                        phase,
                        _topic,
                        _groupId);

                    if (_coordinator.State != RealtimeStreamState.Faulted)
                        _coordinator.EnterFaulted(ex.Message);
                    throw;
                }

                if (_coordinator.State == RealtimeStreamState.Ready)
                    _coordinator.EnterRecovering(ex.Message);

                consecutiveFailures++;
                recreateWithBackoff = true;
                var backoff = ComputeSessionBackoff(consecutiveFailures);
                _logger?.LogWarning(
                    ex,
                    "SSE Kafka push transient failure; recreating session. Phase={Phase} Attempt={Attempt} BackoffMs={BackoffMs} Topic={Topic} Group={Group}",
                    phase,
                    consecutiveFailures,
                    (int)backoff.TotalMilliseconds,
                    _topic,
                    _groupId);
            }
            finally
            {
                session?.Dispose();
            }

            if (!recreateWithBackoff
                || cancellationToken.IsCancellationRequested
                || _coordinator.State == RealtimeStreamState.Faulted)
            {
                continue;
            }

            try
            {
                await DelaySessionBackoffAsync(consecutiveFailures, phase, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private long? PrepareAssignment(
        IRealtimeKafkaConsumerSession session,
        TopicPartition topicPartition,
        bool isFirstSession,
        ref string phase)
    {
        phase = "QueryWatermarks";
        var watermarks = session.QueryWatermarkOffsets(topicPartition, _watermarkTimeout);
        var low = watermarks.Low.Value;
        var high = watermarks.High.Value;

        phase = "Assign";
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

    private readonly record struct SessionRunResult(bool RecreateConsumer, int ConsecutiveFailures);

    // RecreateConsumer = FatalFailure (recrear consumidor); false = fin normal / cancel / Faulted.
    private async Task<SessionRunResult> RunSessionAsync(
        IRealtimeKafkaPushTransport transport,
        FleetRealtimeKafkaPushLoop loop,
        long? baselineForReady,
        int consecutiveFailures,
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
                    consecutiveFailures = 0;
                    var healthyTransition = KafkaPushPollStateMachine.Apply(
                        _coordinator,
                        pollResult,
                        baselineForReady);
                    baselineForReady = healthyTransition.BaselineForReady;
                    break;

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
                        return new SessionRunResult(RecreateConsumer: true, consecutiveFailures);
                    }

                    break;
            }
        }

        return new SessionRunResult(RecreateConsumer: false, consecutiveFailures);
    }

    private Task DelaySessionBackoffAsync(
        int consecutiveFailures,
        string phase,
        CancellationToken cancellationToken)
    {
        var backoff = ComputeSessionBackoff(consecutiveFailures);
        _logger?.LogInformation(
            "SSE Kafka push session backoff. Phase={Phase} Attempt={Attempt} BackoffMs={BackoffMs} Topic={Topic} Group={Group}",
            phase,
            consecutiveFailures,
            (int)backoff.TotalMilliseconds,
            _topic,
            _groupId);
        return _delayAsync(backoff, cancellationToken);
    }

    internal static TimeSpan ComputeSessionBackoff(int consecutiveFailures)
    {
        var attempt = Math.Max(1, consecutiveFailures);
        var shift = Math.Min(attempt - 1, 8);
        var milliseconds = 200L * (1L << shift);
        if (milliseconds > MaxSessionBackoff.TotalMilliseconds)
            return MaxSessionBackoff;

        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
