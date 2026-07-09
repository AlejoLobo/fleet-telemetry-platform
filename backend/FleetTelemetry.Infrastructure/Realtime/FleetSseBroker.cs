using System.Collections.Concurrent;
using System.Threading.Channels;
using FleetTelemetry.Application.DTOs;

// Broker pub/sub para eventos SSE de flota.
namespace FleetTelemetry.Infrastructure.Realtime;

// Distribuye eventos a suscriptores conectados vía SSE.
public sealed class FleetSseBroker
{
    private readonly ConcurrentDictionary<Guid, ChannelWriter<FleetSseEvent>> _subscribers = new();

    // Registra suscriptor y devuelve canal de lectura.
    public ChannelReader<FleetSseEvent> Subscribe(out Guid subscriptionId)
    {
        subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateBounded<FleetSseEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[subscriptionId] = channel.Writer;
        return channel.Reader;
    }

    // Elimina suscriptor y cierra su canal.
    public void Unsubscribe(Guid subscriptionId)
    {
        if (_subscribers.TryRemove(subscriptionId, out var writer))
            writer.TryComplete();
    }

    // Envía evento a todos los suscriptores activos.
    public async Task PublishAsync(FleetSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        foreach (var writer in _subscribers.Values)
            await writer.WriteAsync(sseEvent, cancellationToken);
    }

    public int SubscriberCount => _subscribers.Count;
}
