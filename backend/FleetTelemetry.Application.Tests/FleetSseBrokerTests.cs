using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

public class FleetSseBrokerTests
{
    [Fact]
    public void PublishLocal_drops_events_for_slow_subscriber_without_blocking()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 5);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        for (var i = 0; i < 20; i++)
            broker.PublishLocal("alert", new { index = i });

        var drained = 0;
        while (subscription.LiveReader.TryRead(out _))
            drained++;

        Assert.Equal(5, drained);
        Assert.True(broker.PublishedEvents >= 5);

        broker.Unsubscribe(subscription.SubscriptionId);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.Equal(1, broker.TotalUnsubscribes);
    }

    [Fact]
    public void TryPublishExternal_assigns_kafka_offset_as_stream_id()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(100, "heartbeat", new { ok = true }));
        Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(101, "heartbeat", new { ok = true }));
        Assert.Equal(101, broker.LatestStreamId);
    }

    [Fact]
    public void SubscribeFrom_replays_events_after_cursor()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 50);
        broker.PublishExternal(10, "alert", new { n = 1 });
        broker.PublishExternal(11, "alert", new { n = 2 });
        broker.PublishExternal(12, "alert", new { n = 3 });

        var subscription = broker.SubscribeFrom(new SseLastEventId.ValidCursor(10));

        Assert.Equal(SseReplayStatus.ReplayAvailable, subscription.ReplayStatus);
        Assert.Equal(2, subscription.ReplayEvents.Count);
        Assert.Equal(12, subscription.ReplayEvents[^1].StreamId);
    }

    [Fact]
    public void Unsubscribe_removes_subscriber_without_affecting_others()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 10);
        var first = broker.SubscribeFrom(new SseLastEventId.Missing());
        broker.SubscribeFrom(new SseLastEventId.Missing());

        Assert.Equal(2, broker.SubscriberCount);

        broker.Unsubscribe(first.SubscriptionId);
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
