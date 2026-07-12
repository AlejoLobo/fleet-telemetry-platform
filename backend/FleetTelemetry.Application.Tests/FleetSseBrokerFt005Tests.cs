using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

// FT-005: fan-out multi-réplica, replay atómico y offsets Kafka como StreamId.
public class FleetSseBrokerFt005Tests
{
    [Fact]
    public void Dos_replicas_reciben_el_mismo_evento_Kafka()
    {
        var replicaA = new FleetSseBroker(TimeProvider.System, channelCapacity: 20);
        var replicaB = new FleetSseBroker(TimeProvider.System, channelCapacity: 20);

        var subA = replicaA.SubscribeFrom(0);
        var subB = replicaB.SubscribeFrom(0);

        Assert.True(replicaA.TryPublishExternal(501, "vehicle-update", new { vehicleId = "VH-001" }));
        Assert.True(replicaB.TryPublishExternal(501, "vehicle-update", new { vehicleId = "VH-001" }));

        Assert.True(subA.LiveReader.TryRead(out var eventA));
        Assert.True(subB.LiveReader.TryRead(out var eventB));
        Assert.Equal("VH-001", ((dynamic)eventA.Data).vehicleId);
        Assert.Equal("VH-001", ((dynamic)eventB.Data).vehicleId);
    }

    [Fact]
    public void Dos_replicas_reciben_el_mismo_StreamId()
    {
        var replicaA = new FleetSseBroker(TimeProvider.System);
        var replicaB = new FleetSseBroker(TimeProvider.System);

        replicaA.TryPublishExternal(9001, "alert", new { n = 1 });
        replicaB.TryPublishExternal(9001, "alert", new { n = 1 });

        var subA = replicaA.SubscribeFrom(9000);
        var subB = replicaB.SubscribeFrom(9000);

        Assert.Single(subA.ReplayEvents);
        Assert.Single(subB.ReplayEvents);
        Assert.Equal(9001, subA.ReplayEvents[0].StreamId);
        Assert.Equal(9001, subB.ReplayEvents[0].StreamId);
    }

    [Fact]
    public void Replicas_no_comparten_consumer_group()
    {
        var serviceA = new FleetSseKafkaPushHostedService(
            new FleetSseBroker(TimeProvider.System),
            Microsoft.Extensions.Options.Options.Create(new Infrastructure.Configuration.KafkaOptions
            {
                RealtimeConsumerGroupBase = "fleet-realtime-sse"
            }),
            Microsoft.Extensions.Options.Options.Create(new Infrastructure.Configuration.SseOptions
            {
                InstanceId = "api-1"
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FleetSseKafkaPushHostedService>.Instance);

        var serviceB = new FleetSseKafkaPushHostedService(
            new FleetSseBroker(TimeProvider.System),
            Microsoft.Extensions.Options.Options.Create(new Infrastructure.Configuration.KafkaOptions
            {
                RealtimeConsumerGroupBase = "fleet-realtime-sse"
            }),
            Microsoft.Extensions.Options.Options.Create(new Infrastructure.Configuration.SseOptions
            {
                InstanceId = "api-2"
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FleetSseKafkaPushHostedService>.Instance);

        Assert.Equal("fleet-realtime-sse-api-1", serviceA.ConsumerGroupId);
        Assert.Equal("fleet-realtime-sse-api-2", serviceB.ConsumerGroupId);
        Assert.NotEqual(serviceA.ConsumerGroupId, serviceB.ConsumerGroupId);
    }

    [Fact]
    public void Evento_duplicado_no_se_publica_dos_veces()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var subscription = broker.SubscribeFrom(0);

        Assert.True(broker.TryPublishExternal(77, "alert", new { n = 1 }));
        Assert.False(broker.TryPublishExternal(77, "alert", new { n = 1 }));
        Assert.Equal(1, broker.DuplicateEvents);

        Assert.True(subscription.LiveReader.TryRead(out _));
        Assert.False(subscription.LiveReader.TryRead(out _));
    }

    [Fact]
    public void Offset_no_se_confirma_si_broker_falla()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var shouldCommit = broker.TryPublishExternal(-1, "alert", new { n = 1 });
        Assert.False(shouldCommit);
    }

    [Fact]
    public void Replay_y_live_no_duplican_evento_en_cutover()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 20, replayBufferSize: 20);
        broker.TryPublishExternal(40, "vehicle-update", new { vehicleId = "VH-A" });
        broker.TryPublishExternal(41, "vehicle-update", new { vehicleId = "VH-B" });

        var subscription = broker.SubscribeFrom(39);

        Assert.Equal(2, subscription.ReplayEvents.Count);
        Assert.Equal(41, subscription.CutoverId);

        broker.TryPublishExternal(42, "vehicle-update", new { vehicleId = "VH-C" });

        var delivered = subscription.ReplayEvents.Select(evt => evt.StreamId).ToList();
        while (subscription.LiveReader.TryRead(out var liveEvent))
            delivered.Add(liveEvent.StreamId);

        Assert.Equal(new long[] { 40, 41, 42 }, delivered);
        Assert.Equal(delivered.Count, delivered.Distinct().Count());
    }

    [Fact]
    public void Evento_entre_suscripcion_y_replay_no_se_pierde()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 20, replayBufferSize: 20);
        broker.TryPublishExternal(10, "alert", new { n = 1 });

        var subscription = broker.SubscribeFrom(9);
        broker.TryPublishExternal(11, "alert", new { n = 2 });

        var ids = subscription.ReplayEvents.Select(evt => evt.StreamId).ToList();
        while (subscription.LiveReader.TryRead(out var liveEvent))
            ids.Add(liveEvent.StreamId);

        Assert.Equal(new[] { 10L, 11 }, ids);
    }

    [Fact]
    public void LastEventId_fuera_del_buffer_genera_stream_reset()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 2);
        broker.TryPublishExternal(100, "alert", new { n = 1 });
        broker.TryPublishExternal(101, "alert", new { n = 2 });
        broker.TryPublishExternal(102, "alert", new { n = 3 });

        var subscription = broker.SubscribeFrom(50);

        Assert.Equal(SseReplayStatus.ReplayGap, subscription.ReplayStatus);
        Assert.Equal("replay-gap", subscription.ResetReason);
        Assert.Empty(subscription.ReplayEvents);
    }

    [Fact]
    public void LastEventId_futuro_genera_stream_reset()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.TryPublishExternal(20, "alert", new { n = 1 });

        var subscription = broker.SubscribeFrom(99);

        Assert.Equal(SseReplayStatus.LastEventIdAhead, subscription.ReplayStatus);
        Assert.Equal("invalid-last-event-id", subscription.ResetReason);
    }

    [Fact]
    public void Connected_solo_llega_al_cliente_nuevo()
    {
        // connected se escribe por conexión en EventsController; el broker no lo replica.
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.TryPublishExternal(5, "vehicle-update", new { vehicleId = "VH-001" });

        var subscription = broker.SubscribeFrom(4);
        Assert.DoesNotContain(
            subscription.ReplayEvents,
            evt => evt.EventType == FleetRealtimeEventTypes.Connected);
        Assert.All(subscription.ReplayEvents, evt => Assert.Equal("vehicle-update", evt.EventType));
    }

    [Fact]
    public void Connected_no_se_agrega_al_replay()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.PublishEphemeral("connected", new { status = "connected" });

        var subscription = broker.SubscribeFrom(0);
        Assert.Empty(subscription.ReplayEvents);
        Assert.Equal(0, broker.LatestStreamId);
    }
}
