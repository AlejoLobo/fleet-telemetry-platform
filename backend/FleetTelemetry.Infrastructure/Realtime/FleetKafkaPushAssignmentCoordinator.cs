using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Realtime;

// Estrategia real de asignación/reanudación Kafka→SSE (única fuente de verdad).
internal sealed class FleetKafkaPushAssignmentCoordinator
{
    private readonly FleetSseBroker _broker;
    private readonly IFleetKafkaPushReadiness _readiness;
    private readonly ILogger? _logger;
    private readonly object _sync = new();
    private bool _awaitingReadyAfterAssignment;

    public FleetKafkaPushAssignmentCoordinator(
        FleetSseBroker broker,
        IFleetKafkaPushReadiness readiness,
        ILogger? logger = null)
    {
        _broker = broker;
        _readiness = readiness;
        _logger = logger;
    }

    internal bool AwaitingReadyAfterAssignment
    {
        get
        {
            lock (_sync)
                return _awaitingReadyAfterAssignment;
        }
    }

    public void HandlePartitionsRevoked(IReadOnlyList<TopicPartitionOffset> partitions)
    {
        _ = partitions;
        lock (_sync)
            _awaitingReadyAfterAssignment = false;

        _readiness.MarkRebalancing();
        _logger?.LogWarning(
            "SSE Kafka push rebalancing: partitions revoked. LastProcessed={LastProcessed}",
            _broker.LastProcessedExternalOffset);
    }

    public void HandlePartitionsLost(IReadOnlyList<TopicPartitionOffset> partitions)
    {
        _ = partitions;
        lock (_sync)
            _awaitingReadyAfterAssignment = false;

        EnterFaulted("Kafka partitions lost.");
    }

    public IEnumerable<TopicPartitionOffset> HandlePartitionsAssigned(
        IConsumer<string, string> consumer,
        IReadOnlyList<TopicPartition> partitions)
    {
        _readiness.MarkAssigned();

        if (partitions.Count != 1)
        {
            EnterFaulted($"Expected exactly one partition assignment, got {partitions.Count}.");
            throw new InvalidOperationException(_readiness.FaultReason);
        }

        var partition = partitions[0];
        var resumeOffset = ResolveResumeOffset(consumer, partition);
        lock (_sync)
            _awaitingReadyAfterAssignment = true;

        _logger?.LogInformation(
            "SSE Kafka push assigned. Topic={Topic} Partition={Partition} ResumeOffset={ResumeOffset} FirstAssignment={First}",
            partition.Topic,
            partition.Partition.Value,
            resumeOffset,
            !_readiness.HasCompletedFirstAssignment);

        return [new TopicPartitionOffset(partition, new Offset(resumeOffset))];
    }

    // Tras un ciclo de poll Successful: Ready solo si hay asignación pendiente.
    public void NotifySuccessfulPollCycle()
    {
        bool shouldMarkReady;
        lock (_sync)
        {
            shouldMarkReady = _awaitingReadyAfterAssignment
                && _readiness.State == FleetKafkaPushReadinessState.Assigned
                && _readiness.CurrentResumeOffset.HasValue;
            if (shouldMarkReady)
                _awaitingReadyAfterAssignment = false;
        }

        if (!shouldMarkReady)
            return;

        _readiness.MarkReady();
        _logger?.LogInformation(
            "SSE Kafka push Ready after successful poll. ResumeOffset={ResumeOffset}",
            _readiness.CurrentResumeOffset);
    }

    public void EnterFaulted(string reason)
    {
        lock (_sync)
            _awaitingReadyAfterAssignment = false;

        _readiness.MarkFaulted(reason);
        var closed = _broker.CompleteAllSubscribers(reason);
        _logger?.LogError(
            "SSE Kafka push Faulted. Reason={Reason} ClosedSubscribers={Closed} LastProcessed={LastProcessed}",
            reason,
            closed,
            _broker.LastProcessedExternalOffset);
    }

    // Resolución de offset reutilizable en pruebas unitarias.
    internal long ResolveResumeOffset(
        long? kafkaHighWatermark,
        long lastProcessedExternalOffset,
        long? initialPositionOffset,
        bool hasCompletedFirstAssignment)
    {
        if (!hasCompletedFirstAssignment)
        {
            if (kafkaHighWatermark is null)
                throw new InvalidOperationException("First assignment requires Kafka high watermark.");

            _readiness.EstablishFirstAssignmentPosition(kafkaHighWatermark.Value);
            return kafkaHighWatermark.Value;
        }

        var resume = lastProcessedExternalOffset >= 0
            ? lastProcessedExternalOffset + 1
            : initialPositionOffset
              ?? throw new InvalidOperationException(
                  "Reassignment requires InitialPositionOffset when no events were processed.");

        _readiness.EstablishResumePosition(resume);
        return resume;
    }

    private long ResolveResumeOffset(IConsumer<string, string> consumer, TopicPartition partition)
    {
        if (!_readiness.HasCompletedFirstAssignment)
        {
            var watermarks = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10));
            return ResolveResumeOffset(
                watermarks.High.Value,
                _broker.LastProcessedExternalOffset,
                _readiness.InitialPositionOffset,
                hasCompletedFirstAssignment: false);
        }

        return ResolveResumeOffset(
            kafkaHighWatermark: null,
            lastProcessedExternalOffset: _broker.LastProcessedExternalOffset,
            initialPositionOffset: _readiness.InitialPositionOffset,
            hasCompletedFirstAssignment: true);
    }
}
