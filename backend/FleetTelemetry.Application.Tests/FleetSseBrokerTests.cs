using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

public class FleetSseBrokerTests
{
    [Fact]
    public void Publish_drops_events_for_slow_subscriber_without_blocking()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 5);
        var reader = broker.Subscribe(out var subscriptionId);

        for (var i = 0; i < 20; i++)
        {
            broker.Publish("alert", new { index = i });
        }

        var drained = 0;
        while (reader.TryRead(out _))
            drained++;

        Assert.Equal(5, drained);
        Assert.True(broker.PublishedEvents >= 5);

        broker.Unsubscribe(subscriptionId);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.Equal(1, broker.TotalUnsubscribes);
    }

    [Fact]
    public void Publish_assigns_monotonic_stream_ids()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var first = broker.Publish("heartbeat", new { ok = true });
        var second = broker.Publish("heartbeat", new { ok = true });

        Assert.True(second.StreamId > first.StreamId);
    }

    [Fact]
    public void GetReplayAfter_returns_events_after_cursor()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 50);
        var first = broker.Publish("alert", new { n = 1 });
        broker.Publish("alert", new { n = 2 });
        var third = broker.Publish("alert", new { n = 3 });

        var replay = broker.GetReplayAfter(first.StreamId, maxEvents: 10);

        Assert.Equal(2, replay.Count);
        Assert.Equal(third.StreamId, replay.Last().StreamId);
    }

    [Fact]
    public void Unsubscribe_removes_subscriber_without_affecting_others()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 10);
        broker.Subscribe(out var first);
        broker.Subscribe(out var second);

        Assert.Equal(2, broker.SubscriberCount);

        broker.Unsubscribe(first);
        Assert.Equal(1, broker.SubscriberCount);
        Assert.Equal(2, broker.TotalSubscriptions);
    }

    [Fact]
    public void AlertStreamCursor_orders_equal_timestamps_by_alert_id()
    {
        var timestamp = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
        var first = AlertStreamCursor.FromAlert(timestamp, Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = AlertStreamCursor.FromAlert(timestamp, Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.True(second.IsAfter(first));
        Assert.False(first.IsAfter(second));
    }
}
