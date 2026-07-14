using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Realtime;

namespace FleetTelemetry.Infrastructure.Realtime;

// Distribuye eventos SSE con IDs Kafka (offset), replay acotado y handoff atómico replay→live.
public class FleetSseBroker
{
    private readonly int _channelCapacity;
    private readonly int _replayBufferSize;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<Guid, SubscriberState> _subscribers = new();
    private readonly List<FleetSseEvent> _replayBuffer = [];
    private readonly HashSet<long> _replayIds = [];
    private readonly InvalidOffsetIntervalSet _invalidOffsets;
    private long _latestStreamId;
    private long _lastProcessedExternalOffset = -1;
    private long _firstProcessedExternalOffset = -1;
    private long _nextLocalStreamId = 1;

    private long _publishedEvents;
    private long _overflowEvents;
    private long _totalSubscriptions;
    private long _totalUnsubscribes;
    private long _duplicateEvents;
    private long _outOfOrderEvents;
    private long _invalidCommittedOffsetCount;

    public FleetSseBroker(
        TimeProvider timeProvider,
        int channelCapacity = 100,
        int replayBufferSize = 200,
        int maxInvalidOffsetsCovered = 10_000)
    {
        _timeProvider = timeProvider;
        _channelCapacity = Math.Max(1, channelCapacity);
        _replayBufferSize = Math.Max(10, replayBufferSize);
        _invalidOffsets = new InvalidOffsetIntervalSet(maxInvalidOffsetsCovered);
    }

    public long PublishedEvents => Interlocked.Read(ref _publishedEvents);
    public long OverflowEvents => Interlocked.Read(ref _overflowEvents);
    public long DuplicateEvents => Interlocked.Read(ref _duplicateEvents);
    public long OutOfOrderEvents => Interlocked.Read(ref _outOfOrderEvents);
    public long TotalSubscriptions => Interlocked.Read(ref _totalSubscriptions);
    public long TotalUnsubscribes => Interlocked.Read(ref _totalUnsubscribes);
    public long InvalidCommittedOffsets => Interlocked.Read(ref _invalidCommittedOffsetCount);

    public long LastAcceptedExternalOffset => LastProcessedExternalOffset;

    public long LastProcessedExternalOffset
    {
        get
        {
            lock (_sync)
                return _lastProcessedExternalOffset;
        }
    }

    public long LatestStreamId
    {
        get
        {
            lock (_sync)
                return _latestStreamId;
        }
    }

    public int InvalidOffsetIntervalCount
    {
        get
        {
            lock (_sync)
                return _invalidOffsets.IntervalCount;
        }
    }

    public long InvalidOffsetCoveredCount
    {
        get
        {
            lock (_sync)
                return _invalidOffsets.CoveredOffsetCount;
        }
    }

    public int SubscriberCount => _subscribers.Count;

    public bool IsInvalidCommittedOffset(long streamId)
    {
        lock (_sync)
            return _invalidOffsets.Contains(streamId);
    }

    // Línea base al arrancar: High-1. El live empieza en High.
    public void EstablishBaseline(long baselineOffset)
    {
        lock (_sync)
        {
            _lastProcessedExternalOffset = baselineOffset;
            _firstProcessedExternalOffset = baselineOffset >= 0 ? baselineOffset : -1;
            if (baselineOffset >= 0)
                _latestStreamId = Math.Max(_latestStreamId, baselineOffset);
        }
    }

    // Pérdida por retención Kafka: nueva línea base y replay vacío (obligar snapshot).
    public void ResetToBaseline(long baselineOffset)
    {
        lock (_sync)
        {
            _replayBuffer.Clear();
            _replayIds.Clear();
            _invalidOffsets.Clear();
            _lastProcessedExternalOffset = baselineOffset;
            _firstProcessedExternalOffset = baselineOffset >= 0 ? baselineOffset : -1;
            _latestStreamId = baselineOffset >= 0 ? baselineOffset : 0;
        }
    }

    public SseSubscription SubscribeFrom(SseLastEventId lastEventId)
    {
        lock (_sync)
        {
            var subscriptionId = Guid.NewGuid();
            var cutoverId = ResolveCutoverIdLocked();

            return lastEventId switch
            {
                // Sin Last-Event-ID: forzar snapshot REST antes de live (cutover actual).
                SseLastEventId.Missing => CreateSubscription(
                    subscriptionId,
                    cutoverId,
                    SseReplayStatus.ReplayGap,
                    Array.Empty<FleetSseEvent>(),
                    "initial-snapshot"),
                SseLastEventId.InvalidCursor => CreateSubscription(
                    subscriptionId,
                    cutoverId,
                    SseReplayStatus.LastEventIdAhead,
                    Array.Empty<FleetSseEvent>(),
                    "invalid-last-event-id"),
                SseLastEventId.ValidCursor valid => CreateValidCursorSubscription(
                    subscriptionId,
                    valid.Value,
                    cutoverId),
                _ => throw new InvalidOperationException("Unsupported SSE cursor type.")
            };
        }
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        if (_subscribers.TryRemove(subscriptionId, out var state))
        {
            state.Writer.TryComplete();
            Interlocked.Increment(ref _totalUnsubscribes);
        }
    }

    // Cierra todas las conexiones SSE (Faulted / partición perdida): los clientes deben reconectar.
    public int CompleteAllSubscribers(string reason)
    {
        _ = reason;
        var closed = 0;
        foreach (var pair in _subscribers)
        {
            if (_subscribers.TryRemove(pair.Key, out var state))
            {
                state.Writer.TryComplete();
                Interlocked.Increment(ref _totalUnsubscribes);
                closed++;
            }
        }

        return closed;
    }

    public virtual ExternalPublishResult PublishExternal(
        long streamId,
        string eventType,
        object data,
        DateTimeOffset? timestamp = null)
    {
        if (streamId < 0)
        {
            Interlocked.Increment(ref _outOfOrderEvents);
            return ExternalPublishResult.OutOfOrder;
        }

        lock (_sync)
        {
            var dedup = EvaluateExternalDedupLocked(streamId);
            if (dedup is not null)
                return dedup.Value;

            if (_replayIds.Contains(streamId))
            {
                Interlocked.Increment(ref _duplicateEvents);
                return ExternalPublishResult.Duplicate;
            }

            var sseEvent = new FleetSseEvent(
                streamId,
                eventType,
                data,
                timestamp ?? _timeProvider.GetUtcNow());

            EnqueueReplayLocked(sseEvent);
            AdvanceWatermarkLocked(streamId);
            _latestStreamId = Math.Max(_latestStreamId, streamId);

            FanOutLiveLocked(sseEvent);
            return ExternalPublishResult.Accepted;
        }
    }

    // Eventos locales del modo polling con ID autogenerado (no Kafka).
    public FleetSseEvent PublishLocal(string eventType, object data, DateTimeOffset? timestamp = null)
    {
        lock (_sync)
        {
            var streamId = Interlocked.Increment(ref _nextLocalStreamId);
            var sseEvent = new FleetSseEvent(
                streamId,
                eventType,
                data,
                timestamp ?? _timeProvider.GetUtcNow());

            EnqueueReplayLocked(sseEvent);
            _latestStreamId = Math.Max(_latestStreamId, streamId);
            FanOutLiveLocked(sseEvent);
            return sseEvent;
        }
    }

    // Heartbeat u otros eventos efímeros: solo live, sin replay ni ID global Kafka.
    public void PublishEphemeral(string eventType, object data, DateTimeOffset? timestamp = null)
    {
        var sseEvent = new FleetSseEvent(
            -1,
            eventType,
            data,
            timestamp ?? _timeProvider.GetUtcNow());

        lock (_sync)
            FanOutLiveLocked(sseEvent);
    }

    // Fuerza stream-reset en todos los suscriptores activos (payload inválido, etc.).
    public void PublishStreamResetToAll(string reason)
    {
        long? latestEventId;
        lock (_sync)
        {
            var cutoverId = ResolveCutoverIdLocked();
            latestEventId = cutoverId >= 0 ? cutoverId : null;
        }

        PublishEphemeral(
            FleetRealtimeEventTypes.StreamReset,
            new SseStreamResetData(
                reason,
                latestEventId?.ToString(CultureInfo.InvariantCulture)),
            _timeProvider.GetUtcNow());
    }

    // Avanza el watermark Kafka inválido sin publicar evento ni reintentar el mismo mensaje.
    public ExternalPublishResult RecordInvalidExternalOffset(long streamId)
    {
        if (streamId < 0)
        {
            Interlocked.Increment(ref _outOfOrderEvents);
            return ExternalPublishResult.OutOfOrder;
        }

        lock (_sync)
        {
            if (_invalidOffsets.Contains(streamId))
            {
                Interlocked.Increment(ref _duplicateEvents);
                return ExternalPublishResult.Duplicate;
            }

            var dedup = EvaluateExternalDedupLocked(streamId);
            if (dedup is not null)
                return dedup.Value;

            _invalidOffsets.Add(streamId);
            Interlocked.Increment(ref _invalidCommittedOffsetCount);
            AdvanceWatermarkLocked(streamId);
            return ExternalPublishResult.Accepted;
        }
    }

    public void PruneStaleSubscribers(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var staleBefore = _timeProvider.GetUtcNow() - TimeSpan.FromMinutes(30);
        foreach (var pair in _subscribers)
        {
            if (pair.Value.LastActivity < staleBefore)
                Unsubscribe(pair.Key);
        }
    }

    private long ResolveCutoverIdLocked()
    {
        if (_lastProcessedExternalOffset >= 0)
            return _lastProcessedExternalOffset;

        return _replayBuffer.Count == 0 ? -1L : _latestStreamId;
    }

    private ExternalPublishResult? EvaluateExternalDedupLocked(long streamId)
    {
        if (streamId <= _lastProcessedExternalOffset)
        {
            if (_firstProcessedExternalOffset >= 0 && streamId >= _firstProcessedExternalOffset)
            {
                Interlocked.Increment(ref _duplicateEvents);
                return ExternalPublishResult.Duplicate;
            }

            Interlocked.Increment(ref _outOfOrderEvents);
            return ExternalPublishResult.OutOfOrder;
        }

        return null;
    }

    private void AdvanceWatermarkLocked(long streamId)
    {
        if (_firstProcessedExternalOffset < 0)
            _firstProcessedExternalOffset = streamId;
        _lastProcessedExternalOffset = streamId;

        // Retención acotada: descarta gaps antiguos fuera de la ventana del replay.
        var retentionFloor = _lastProcessedExternalOffset - Math.Max(_replayBufferSize * 4L, 1_000L);
        if (retentionFloor >= 0)
            _invalidOffsets.PruneBeforeOrEqual(retentionFloor);
    }

    private SseSubscription CreateValidCursorSubscription(
        Guid subscriptionId,
        long lastEventId,
        long cutoverId)
    {
        var replayStatus = DetermineReplayStatus(lastEventId, cutoverId, out var resetReason);
        var replayEvents = replayStatus == SseReplayStatus.ReplayAvailable
            ? BuildReplay(lastEventId, cutoverId)
            : Array.Empty<FleetSseEvent>();

        return CreateSubscription(subscriptionId, cutoverId, replayStatus, replayEvents, resetReason);
    }

    private SseSubscription CreateSubscription(
        Guid subscriptionId,
        long cutoverId,
        SseReplayStatus replayStatus,
        IReadOnlyList<FleetSseEvent> replayEvents,
        string? resetReason)
    {
        var channel = CreateChannel();
        _subscribers[subscriptionId] = new SubscriberState(
            channel.Writer,
            cutoverId,
            _timeProvider.GetUtcNow());

        Interlocked.Increment(ref _totalSubscriptions);

        return new SseSubscription
        {
            SubscriptionId = subscriptionId,
            ReplayStatus = replayStatus,
            ReplayEvents = replayEvents,
            LiveReader = channel.Reader,
            CutoverId = cutoverId,
            LatestEventId = cutoverId >= 0
                ? cutoverId.ToString(CultureInfo.InvariantCulture)
                : null,
            ResetReason = resetReason
        };
    }

    private void FanOutLiveLocked(FleetSseEvent sseEvent)
    {
        List<Guid>? overflowed = null;

        foreach (var pair in _subscribers)
        {
            if (sseEvent.StreamId >= 0 && sseEvent.StreamId <= pair.Value.CutoverId)
                continue;

            if (pair.Value.Writer.TryWrite(sseEvent))
            {
                Interlocked.Increment(ref _publishedEvents);
                pair.Value.Touch(_timeProvider.GetUtcNow());
                continue;
            }

            Interlocked.Increment(ref _overflowEvents);
            overflowed ??= [];
            overflowed.Add(pair.Key);
        }

        if (overflowed is null)
            return;

        foreach (var subscriptionId in overflowed)
            Unsubscribe(subscriptionId);
    }

    private SseReplayStatus DetermineReplayStatus(long lastEventId, long cutoverId, out string? resetReason)
    {
        resetReason = null;

        if (cutoverId < 0)
        {
            resetReason = "instance-restarted";
            return SseReplayStatus.ReplayGap;
        }

        if (lastEventId > cutoverId)
        {
            resetReason = "invalid-last-event-id";
            return SseReplayStatus.LastEventIdAhead;
        }

        // Cualquier offset inválido en (lastEventId, cutoverId], incluso con buffer parcial.
        if (_invalidOffsets.HasAnyInOpenClosedRange(lastEventId, cutoverId))
        {
            resetReason = "invalid-payload-gap";
            return SseReplayStatus.ReplayGap;
        }

        var firstBufferedId = _replayBuffer.Count == 0 ? (long?)null : _replayBuffer[0].StreamId;
        if (firstBufferedId is null)
        {
            // Watermark sin eventos reproducibles (p. ej. solo inválidos): al día si cursor == watermark.
            if (lastEventId == cutoverId)
                return SseReplayStatus.ReplayAvailable;

            resetReason = "instance-restarted";
            return SseReplayStatus.ReplayGap;
        }

        if (lastEventId + 1 < firstBufferedId.Value && lastEventId < cutoverId)
        {
            resetReason = "replay-gap";
            return SseReplayStatus.ReplayGap;
        }

        return SseReplayStatus.ReplayAvailable;
    }

    private IReadOnlyList<FleetSseEvent> BuildReplay(long lastEventId, long cutoverId) =>
        _replayBuffer
            .Where(evt => evt.StreamId > lastEventId && evt.StreamId <= cutoverId)
            .OrderBy(evt => evt.StreamId)
            .ToArray();

    private void EnqueueReplayLocked(FleetSseEvent sseEvent)
    {
        _replayBuffer.Add(sseEvent);
        _replayIds.Add(sseEvent.StreamId);
        _replayBuffer.Sort((left, right) => left.StreamId.CompareTo(right.StreamId));

        while (_replayBuffer.Count > _replayBufferSize)
        {
            var removed = _replayBuffer[0];
            _replayBuffer.RemoveAt(0);
            _replayIds.Remove(removed.StreamId);
        }
    }

    private Channel<FleetSseEvent> CreateChannel() =>
        Channel.CreateBounded<FleetSseEvent>(new BoundedChannelOptions(_channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private sealed class SubscriberState(ChannelWriter<FleetSseEvent> writer, long cutoverId, DateTimeOffset createdAt)
    {
        public ChannelWriter<FleetSseEvent> Writer { get; } = writer;
        public long CutoverId { get; } = cutoverId;
        public DateTimeOffset LastActivity { get; private set; } = createdAt;

        public void Touch(DateTimeOffset activityAt) => LastActivity = activityAt;
    }
}
