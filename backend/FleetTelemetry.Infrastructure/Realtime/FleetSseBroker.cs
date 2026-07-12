using System.Collections.Concurrent;
using System.Threading.Channels;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Realtime;

namespace FleetTelemetry.Infrastructure.Realtime;

// Distribuye eventos SSE con IDs Kafka (offset), replay acotado y handoff atómico replay→live.
public sealed class FleetSseBroker
{
    private readonly int _channelCapacity;
    private readonly int _replayBufferSize;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<Guid, SubscriberState> _subscribers = new();
    private readonly List<FleetSseEvent> _replayBuffer = [];
    private readonly HashSet<long> _replayIds = [];
    private long _latestStreamId;
    private long _nextLocalStreamId = 1;

    private long _publishedEvents;
    private long _droppedEvents;
    private long _totalSubscriptions;
    private long _totalUnsubscribes;
    private long _duplicateEvents;

    public FleetSseBroker(TimeProvider timeProvider, int channelCapacity = 100, int replayBufferSize = 200)
    {
        _timeProvider = timeProvider;
        _channelCapacity = Math.Max(1, channelCapacity);
        _replayBufferSize = Math.Max(10, replayBufferSize);
    }

    public long PublishedEvents => Interlocked.Read(ref _publishedEvents);
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);
    public long DuplicateEvents => Interlocked.Read(ref _duplicateEvents);
    public long TotalSubscriptions => Interlocked.Read(ref _totalSubscriptions);
    public long TotalUnsubscribes => Interlocked.Read(ref _totalUnsubscribes);
    public long LatestStreamId
    {
        get
        {
            lock (_sync)
                return _latestStreamId;
        }
    }

    public int SubscriberCount => _subscribers.Count;

    public SseSubscription SubscribeFrom(long lastEventId)
    {
        lock (_sync)
        {
            var subscriptionId = Guid.NewGuid();
            // -1 indica broker vacío: el primer offset Kafka (0) debe entrar por live.
            var cutoverId = _replayBuffer.Count == 0 ? -1L : _latestStreamId;
            var replayStatus = DetermineReplayStatus(lastEventId, cutoverId, out var resetReason);
            var replayEvents = replayStatus == SseReplayStatus.ReplayAvailable
                ? BuildReplay(lastEventId, cutoverId)
                : Array.Empty<FleetSseEvent>();

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
                LatestEventId = cutoverId >= 0 ? cutoverId : null,
                ResetReason = resetReason
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

    // Publica evento con StreamId externo (offset Kafka). Retorna false si es duplicado.
    public bool TryPublishExternal(long streamId, string eventType, object data, DateTimeOffset? timestamp = null)
    {
        if (streamId < 0)
            return false;

        FleetSseEvent? sseEvent = null;
        var accepted = false;

        lock (_sync)
        {
            if (_replayIds.Contains(streamId))
            {
                Interlocked.Increment(ref _duplicateEvents);
                return false;
            }

            sseEvent = new FleetSseEvent(
                streamId,
                eventType,
                data,
                timestamp ?? _timeProvider.GetUtcNow());

            EnqueueReplayLocked(sseEvent);
            _latestStreamId = Math.Max(_latestStreamId, streamId);
            accepted = true;
        }

        if (!accepted || sseEvent is null)
        {
            Interlocked.Increment(ref _duplicateEvents);
            return false;
        }

        FanOutLive(sseEvent);
        return true;
    }

    // Eventos locales del modo polling con ID autogenerado (no Kafka).
    public FleetSseEvent PublishLocal(string eventType, object data, DateTimeOffset? timestamp = null)
    {
        FleetSseEvent sseEvent;

        lock (_sync)
        {
            var streamId = Interlocked.Increment(ref _nextLocalStreamId);
            sseEvent = new FleetSseEvent(
                streamId,
                eventType,
                data,
                timestamp ?? _timeProvider.GetUtcNow());

            EnqueueReplayLocked(sseEvent);
            _latestStreamId = Math.Max(_latestStreamId, streamId);
        }

        FanOutLive(sseEvent);
        return sseEvent;
    }

    // Heartbeat u otros eventos efímeros: solo live, sin replay ni ID global Kafka.
    public void PublishEphemeral(string eventType, object data, DateTimeOffset? timestamp = null)
    {
        var sseEvent = new FleetSseEvent(
            -1,
            eventType,
            data,
            timestamp ?? _timeProvider.GetUtcNow());

        foreach (var pair in _subscribers)
        {
            if (pair.Value.Writer.TryWrite(sseEvent))
                Interlocked.Increment(ref _publishedEvents);
            else
                Interlocked.Increment(ref _droppedEvents);
        }
    }

    public void PruneStaleSubscribers()
    {
        var staleBefore = _timeProvider.GetUtcNow() - TimeSpan.FromMinutes(30);
        foreach (var pair in _subscribers)
        {
            if (pair.Value.LastActivity < staleBefore)
                Unsubscribe(pair.Key);
        }
    }

    private void FanOutLive(FleetSseEvent sseEvent)
    {
        foreach (var pair in _subscribers)
        {
            if (sseEvent.StreamId >= 0 && sseEvent.StreamId <= pair.Value.CutoverId)
                continue;

            if (pair.Value.Writer.TryWrite(sseEvent))
            {
                Interlocked.Increment(ref _publishedEvents);
                pair.Value.Touch(_timeProvider.GetUtcNow());
            }
            else
            {
                Interlocked.Increment(ref _droppedEvents);
            }
        }
    }

    private SseReplayStatus DetermineReplayStatus(long lastEventId, long cutoverId, out string? resetReason)
    {
        resetReason = null;

        if (lastEventId < 0)
        {
            resetReason = "invalid-last-event-id";
            return SseReplayStatus.LastEventIdAhead;
        }

        if (lastEventId == 0)
            return SseReplayStatus.ReplayAvailable;

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
            FullMode = BoundedChannelFullMode.DropWrite,
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
