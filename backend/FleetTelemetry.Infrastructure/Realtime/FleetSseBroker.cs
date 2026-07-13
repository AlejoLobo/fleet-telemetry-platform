using System.Collections.Concurrent;
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
    private readonly HashSet<long> _invalidCommittedOffsets = [];
    private long _latestStreamId;
    private long _lastAcceptedExternalOffset = -1;
    private long _firstAcceptedExternalOffset = -1;
    private long _nextLocalStreamId = 1;

    private long _publishedEvents;
    private long _overflowEvents;
    private long _totalSubscriptions;
    private long _totalUnsubscribes;
    private long _duplicateEvents;
    private long _outOfOrderEvents;

    public FleetSseBroker(TimeProvider timeProvider, int channelCapacity = 100, int replayBufferSize = 200)
    {
        _timeProvider = timeProvider;
        _channelCapacity = Math.Max(1, channelCapacity);
        _replayBufferSize = Math.Max(10, replayBufferSize);
    }

    public long PublishedEvents => Interlocked.Read(ref _publishedEvents);
    public long OverflowEvents => Interlocked.Read(ref _overflowEvents);
    public long DuplicateEvents => Interlocked.Read(ref _duplicateEvents);
    public long OutOfOrderEvents => Interlocked.Read(ref _outOfOrderEvents);
    public long TotalSubscriptions => Interlocked.Read(ref _totalSubscriptions);
    public long TotalUnsubscribes => Interlocked.Read(ref _totalUnsubscribes);
    public long LastAcceptedExternalOffset
    {
        get
        {
            lock (_sync)
                return _lastAcceptedExternalOffset;
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

    public long InvalidCommittedOffsets => Interlocked.Read(ref _invalidCommittedOffsetCount);
    private long _invalidCommittedOffsetCount;

    public int SubscriberCount => _subscribers.Count;

    public bool IsInvalidCommittedOffset(long streamId)
    {
        lock (_sync)
            return _invalidCommittedOffsets.Contains(streamId);
    }

    public SseSubscription SubscribeFrom(SseLastEventId lastEventId)
    {
        lock (_sync)
        {
            var subscriptionId = Guid.NewGuid();
            var cutoverId = _replayBuffer.Count == 0 ? -1L : _latestStreamId;

            return lastEventId switch
            {
                SseLastEventId.Missing => CreateSubscription(
                    subscriptionId,
                    cutoverId,
                    SseReplayStatus.ReplayAvailable,
                    Array.Empty<FleetSseEvent>(),
                    null),
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
            if (streamId <= _lastAcceptedExternalOffset)
            {
                if (_firstAcceptedExternalOffset >= 0 && streamId >= _firstAcceptedExternalOffset)
                {
                    Interlocked.Increment(ref _duplicateEvents);
                    return ExternalPublishResult.Duplicate;
                }

                Interlocked.Increment(ref _outOfOrderEvents);
                return ExternalPublishResult.OutOfOrder;
            }

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
            if (_firstAcceptedExternalOffset < 0)
                _firstAcceptedExternalOffset = streamId;
            _lastAcceptedExternalOffset = streamId;
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
            latestEventId = _latestStreamId >= 0 ? _latestStreamId : null;

        PublishEphemeral(
            FleetRealtimeEventTypes.StreamReset,
            new SseStreamResetData(
                reason,
                latestEventId?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _timeProvider.GetUtcNow());
    }

    // Avanza el offset Kafka inválido sin publicar evento ni reintentar el mismo mensaje.
    public ExternalPublishResult RecordInvalidExternalOffset(long streamId)
    {
        if (streamId < 0)
        {
            Interlocked.Increment(ref _outOfOrderEvents);
            return ExternalPublishResult.OutOfOrder;
        }

        lock (_sync)
        {
            if (_invalidCommittedOffsets.Contains(streamId))
            {
                Interlocked.Increment(ref _duplicateEvents);
                return ExternalPublishResult.Duplicate;
            }

            if (streamId <= _lastAcceptedExternalOffset)
            {
                if (_firstAcceptedExternalOffset >= 0 && streamId >= _firstAcceptedExternalOffset)
                {
                    Interlocked.Increment(ref _duplicateEvents);
                    return ExternalPublishResult.Duplicate;
                }

                Interlocked.Increment(ref _outOfOrderEvents);
                return ExternalPublishResult.OutOfOrder;
            }

            _invalidCommittedOffsets.Add(streamId);
            Interlocked.Increment(ref _invalidCommittedOffsetCount);
            if (_firstAcceptedExternalOffset < 0)
                _firstAcceptedExternalOffset = streamId;
            _lastAcceptedExternalOffset = streamId;
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
                ? cutoverId.ToString(System.Globalization.CultureInfo.InvariantCulture)
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

        var firstBufferedId = _replayBuffer.Count == 0 ? (long?)null : _replayBuffer[0].StreamId;
        if (firstBufferedId is null)
        {
            resetReason = "instance-restarted";
            return SseReplayStatus.ReplayGap;
        }

        if (lastEventId > cutoverId)
        {
            resetReason = "invalid-last-event-id";
            return SseReplayStatus.LastEventIdAhead;
        }

        if (lastEventId + 1 < firstBufferedId.Value)
        {
            for (var missing = lastEventId + 1; missing < firstBufferedId.Value; missing++)
            {
                if (_invalidCommittedOffsets.Contains(missing))
                {
                    resetReason = "invalid-payload-gap";
                    return SseReplayStatus.ReplayGap;
                }
            }

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
