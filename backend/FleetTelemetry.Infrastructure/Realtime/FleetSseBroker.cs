using System.Collections.Concurrent;
using System.Threading.Channels;
using FleetTelemetry.Application.DTOs;

// Broker pub/sub para eventos SSE de flota.
namespace FleetTelemetry.Infrastructure.Realtime;

// Distribuye eventos a suscriptores conectados vía SSE.
public sealed class FleetSseBroker
{
    private readonly int _channelCapacity;
    private readonly ConcurrentDictionary<Guid, SubscriberState> _subscribers = new();

    public long PublishedEvents => Interlocked.Read(ref _publishedEvents);
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    private long _publishedEvents;
    private long _droppedEvents;

    public FleetSseBroker(int channelCapacity = 100)
    {
        _channelCapacity = Math.Max(1, channelCapacity);
    }

    // Registra suscriptor y devuelve canal de lectura.
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
        return channel.Reader;
    }

    // Elimina suscriptor y cierra su canal.
    public void Unsubscribe(Guid subscriptionId)
    {
        if (_subscribers.TryRemove(subscriptionId, out var state))
            state.Writer.TryComplete();
    }

    // Publica sin bloquear; descarta eventos para suscriptores lentos.
    public void Publish(FleetSseEvent sseEvent)
    {
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
    }

    // Cierra suscriptores inactivos para evitar fugas de memoria.
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

    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private sealed class SubscriberState(ChannelWriter<FleetSseEvent> writer, DateTimeOffset createdAt)
    {
        public ChannelWriter<FleetSseEvent> Writer { get; } = writer;
        public DateTimeOffset LastActivity { get; private set; } = createdAt;

        public void Touch(DateTimeOffset activityAt) => LastActivity = activityAt;
    }
}
