using System.Collections.Concurrent;
using System.Threading.Channels;
using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Infrastructure.Realtime;

// Distribuye eventos SSE con IDs monotónicos, replay limitado y backpressure por suscriptor.
public sealed class FleetSseBroker
{
    private readonly int _channelCapacity;
    private readonly int _replayBufferSize;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, SubscriberState> _subscribers = new();
    private readonly ConcurrentQueue<FleetSseEvent> _replayBuffer = new();
    private long _nextStreamId = 1;

    private long _publishedEvents;
    private long _droppedEvents;
    private long _totalSubscriptions;
    private long _totalUnsubscribes;

    public FleetSseBroker(TimeProvider timeProvider, int channelCapacity = 100, int replayBufferSize = 200)
    {
        _timeProvider = timeProvider;
        _channelCapacity = Math.Max(1, channelCapacity);
        _replayBufferSize = Math.Max(10, replayBufferSize);
    }

    public long PublishedEvents => Interlocked.Read(ref _publishedEvents);
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);
    public long TotalSubscriptions => Interlocked.Read(ref _totalSubscriptions);
    public long TotalUnsubscribes => Interlocked.Read(ref _totalUnsubscribes);
    public long LatestStreamId => Interlocked.Read(ref _nextStreamId) - 1;

    public ChannelReader<FleetSseEvent> Subscribe(out Guid subscriptionId)
    {
        subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateBounded<FleetSseEvent>(new BoundedChannelOptions(_channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        _subscribers[subscriptionId] = new SubscriberState(channel.Writer, _timeProvider.GetUtcNow());
        Interlocked.Increment(ref _totalSubscriptions);
        return channel.Reader;
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        if (_subscribers.TryRemove(subscriptionId, out var state))
        {
            state.Writer.TryComplete();
            Interlocked.Increment(ref _totalUnsubscribes);
        }
    }

    public FleetSseEvent Publish(string eventType, object data, DateTimeOffset? timestamp = null)
    {
        var streamId = Interlocked.Increment(ref _nextStreamId);
        var sseEvent = new FleetSseEvent(
            streamId,
            eventType,
            data,
            timestamp ?? _timeProvider.GetUtcNow());

        EnqueueReplay(sseEvent);

        foreach (var pair in _subscribers)
        {
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

        return sseEvent;
    }

    public IReadOnlyList<FleetSseEvent> GetReplayAfter(long lastEventId, int maxEvents)
    {
        var replay = _replayBuffer.ToArray()
            .Where(evt => evt.StreamId > lastEventId)
            .OrderBy(evt => evt.StreamId)
            .Take(Math.Max(1, maxEvents))
            .ToArray();

        return replay;
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

    public int SubscriberCount => _subscribers.Count;

    private void EnqueueReplay(FleetSseEvent sseEvent)
    {
        _replayBuffer.Enqueue(sseEvent);
        while (_replayBuffer.Count > _replayBufferSize && _replayBuffer.TryDequeue(out _))
        {
            // Mantener buffer acotado.
        }
    }

    private sealed class SubscriberState(ChannelWriter<FleetSseEvent> writer, DateTimeOffset createdAt)
    {
        public ChannelWriter<FleetSseEvent> Writer { get; } = writer;
        public DateTimeOffset LastActivity { get; private set; } = createdAt;

        public void Touch(DateTimeOffset activityAt) => LastActivity = activityAt;
    }
}
