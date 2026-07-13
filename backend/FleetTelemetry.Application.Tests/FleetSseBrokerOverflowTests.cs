using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

public class FleetSseBrokerOverflowTests
{
    private static SseLastEventId Valid(long value) => new SseLastEventId.ValidCursor(value);

    [Fact]
    public void Canal_lleno_cierra_la_suscripcion()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 5);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        for (var i = 0; i < 20; i++)
            broker.PublishLocal("alert", new { index = i });

        Assert.Equal(0, broker.SubscriberCount);
        Assert.True(broker.OverflowEvents > 0);
        Assert.Equal(1, broker.TotalUnsubscribes);
        broker.Unsubscribe(subscription.SubscriptionId);
    }

    [Fact]
    public void Overflow_no_continua_con_huecos_silenciosos()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 3, replayBufferSize: 50);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        broker.PublishExternal(1, "alert", new { n = 1 });
        broker.PublishExternal(2, "alert", new { n = 2 });
        broker.PublishExternal(3, "alert", new { n = 3 });
        broker.PublishExternal(4, "alert", new { n = 4 });

        var delivered = new List<long>();
        while (subscription.LiveReader.TryRead(out var evt))
            delivered.Add(evt.StreamId);

        Assert.True(broker.OverflowEvents > 0);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.Equal(delivered.Count, delivered.Distinct().Count());
        for (var i = 1; i < delivered.Count; i++)
            Assert.True(delivered[i] > delivered[i - 1]);
    }

    [Fact]
    public void Reconexion_despues_de_overflow_recupera_eventos_por_replay()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 2, replayBufferSize: 50);
        var first = broker.SubscribeFrom(new SseLastEventId.Missing());

        broker.PublishExternal(10, "alert", new { n = 10 });
        broker.PublishExternal(11, "alert", new { n = 11 });
        broker.PublishExternal(12, "alert", new { n = 12 });

        Assert.Equal(0, broker.SubscriberCount);
        broker.Unsubscribe(first.SubscriptionId);

        broker.PublishExternal(13, "alert", new { n = 13 });

        var replay = broker.SubscribeFrom(Valid(11));
        Assert.Equal(SseReplayStatus.ReplayAvailable, replay.ReplayStatus);
        Assert.Equal(new[] { 12L, 13 }, replay.ReplayEvents.Select(evt => evt.StreamId).ToArray());
    }

    [Fact]
    public void Overflow_fuera_del_buffer_genera_stream_reset()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 2, replayBufferSize: 2);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        broker.PublishExternal(100, "alert", new { n = 1 });
        broker.PublishExternal(101, "alert", new { n = 2 });
        broker.PublishExternal(102, "alert", new { n = 3 });
        broker.PublishExternal(103, "alert", new { n = 4 });

        Assert.Equal(0, broker.SubscriberCount);
        broker.Unsubscribe(subscription.SubscriptionId);

        var reconnect = broker.SubscribeFrom(Valid(50));
        Assert.Equal(SseReplayStatus.ReplayGap, reconnect.ReplayStatus);
        Assert.Equal("replay-gap", reconnect.ResetReason);
    }
}
