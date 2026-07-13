using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

// FT-005: fan-out multi-réplica, replay atómico y offsets Kafka como StreamId.
public class FleetSseBrokerFt005Tests
{
    private static SseLastEventId Valid(long value) => new SseLastEventId.ValidCursor(value);

    [Fact]
    public void Dos_replicas_reciben_el_mismo_evento_Kafka()
    {
        var replicaA = new FleetSseBroker(TimeProvider.System, channelCapacity: 20);
        var replicaB = new FleetSseBroker(TimeProvider.System, channelCapacity: 20);

        var subA = replicaA.SubscribeFrom(new SseLastEventId.Missing());
        var subB = replicaB.SubscribeFrom(new SseLastEventId.Missing());

        Assert.Equal(ExternalPublishResult.Accepted, replicaA.PublishExternal(501, "vehicle-update", new { vehicleId = "VH-001" }));
        Assert.Equal(ExternalPublishResult.Accepted, replicaB.PublishExternal(501, "vehicle-update", new { vehicleId = "VH-001" }));

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

        replicaA.PublishExternal(9001, "alert", new { n = 1 });
        replicaB.PublishExternal(9001, "alert", new { n = 1 });

        var subA = replicaA.SubscribeFrom(Valid(9000));
        var subB = replicaB.SubscribeFrom(Valid(9000));

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
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(77, "alert", new { n = 1 }));
        Assert.Equal(ExternalPublishResult.Duplicate, broker.PublishExternal(77, "alert", new { n = 1 }));
        Assert.Equal(1, broker.DuplicateEvents);

        Assert.True(subscription.LiveReader.TryRead(out _));
        Assert.False(subscription.LiveReader.TryRead(out _));
    }

    [Fact]
    public void Replay_y_live_no_duplican_evento_en_cutover()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 20, replayBufferSize: 20);
        broker.PublishExternal(40, "vehicle-update", new { vehicleId = "VH-A" });
        broker.PublishExternal(41, "vehicle-update", new { vehicleId = "VH-B" });

        var subscription = broker.SubscribeFrom(Valid(39));

        Assert.Equal(2, subscription.ReplayEvents.Count);
        Assert.Equal(41, subscription.CutoverId);

        broker.PublishExternal(42, "vehicle-update", new { vehicleId = "VH-C" });

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
        broker.PublishExternal(10, "alert", new { n = 1 });

        var subscription = broker.SubscribeFrom(Valid(9));
        broker.PublishExternal(11, "alert", new { n = 2 });

        var ids = subscription.ReplayEvents.Select(evt => evt.StreamId).ToList();
        while (subscription.LiveReader.TryRead(out var liveEvent))
            ids.Add(liveEvent.StreamId);

        Assert.Equal(new[] { 10L, 11 }, ids);
    }

    [Fact]
    public void LastEventId_fuera_del_buffer_genera_stream_reset()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 2);
        broker.PublishExternal(100, "alert", new { n = 1 });
        broker.PublishExternal(101, "alert", new { n = 2 });
        broker.PublishExternal(102, "alert", new { n = 3 });

        var subscription = broker.SubscribeFrom(Valid(50));

        Assert.Equal(SseReplayStatus.ReplayGap, subscription.ReplayStatus);
        Assert.Equal("replay-gap", subscription.ResetReason);
        Assert.Empty(subscription.ReplayEvents);
    }

    [Fact]
    public void LastEventId_futuro_genera_stream_reset()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.PublishExternal(20, "alert", new { n = 1 });

        var subscription = broker.SubscribeFrom(Valid(99));

        Assert.Equal(SseReplayStatus.LastEventIdAhead, subscription.ReplayStatus);
        Assert.Equal("invalid-last-event-id", subscription.ResetReason);
    }

    [Fact]
    public void Connected_solo_llega_al_cliente_nuevo()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.PublishExternal(5, "vehicle-update", new { vehicleId = "VH-001" });

        var subscription = broker.SubscribeFrom(Valid(4));
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

        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());
        Assert.Empty(subscription.ReplayEvents);
        Assert.Equal(0, broker.LatestStreamId);
    }

    [Fact]
    public void Sin_Last_Event_ID_no_reproduce_buffer_antiguo()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 20);
        broker.PublishExternal(10, "alert", new { n = 1 });
        broker.PublishExternal(11, "alert", new { n = 2 });

        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        Assert.Equal(SseReplayStatus.ReplayAvailable, subscription.ReplayStatus);
        Assert.Empty(subscription.ReplayEvents);
        Assert.Equal(11, subscription.CutoverId);

        broker.PublishExternal(12, "alert", new { n = 3 });
        Assert.True(subscription.LiveReader.TryRead(out var live));
        Assert.Equal(12, live.StreamId);
    }

    [Fact]
    public void LastEventId_cero_reproduce_offset_uno_si_esta_disponible()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 20);
        broker.PublishExternal(0, "alert", new { n = 0 });
        broker.PublishExternal(1, "alert", new { n = 1 });

        var subscription = broker.SubscribeFrom(Valid(0));

        Assert.Equal(SseReplayStatus.ReplayAvailable, subscription.ReplayStatus);
        Assert.Single(subscription.ReplayEvents);
        Assert.Equal(1, subscription.ReplayEvents[0].StreamId);
    }

    [Fact]
    public void LastEventId_cero_con_buffer_iniciado_en_cien_genera_replay_gap()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 20);
        broker.PublishExternal(100, "alert", new { n = 1 });
        broker.PublishExternal(101, "alert", new { n = 2 });

        var subscription = broker.SubscribeFrom(Valid(0));

        Assert.Equal(SseReplayStatus.ReplayGap, subscription.ReplayStatus);
        Assert.Equal("replay-gap", subscription.ResetReason);
        Assert.Empty(subscription.ReplayEvents);
    }

    [Fact]
    public void Reconexion_sin_cursor_despues_de_stream_reset_no_reproduce_historial_incompleto()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 20);
        broker.PublishExternal(30, "alert", new { n = 1 });
        broker.PublishExternal(31, "alert", new { n = 2 });

        var afterReset = broker.SubscribeFrom(new SseLastEventId.Missing());

        Assert.Empty(afterReset.ReplayEvents);
        Assert.Equal(31, afterReset.CutoverId);
        Assert.Equal(SseReplayStatus.ReplayAvailable, afterReset.ReplayStatus);
    }

    [Fact]
    public void Offset_cero_almacenado_se_trata_como_id_real()
    {
        var parsed = SseLastEventId.Parse("0");
        Assert.IsType<SseLastEventId.ValidCursor>(parsed);
        Assert.Equal(0, ((SseLastEventId.ValidCursor)parsed).Value);

        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 20);
        broker.PublishExternal(0, "alert", new { n = 0 });
        broker.PublishExternal(1, "alert", new { n = 1 });

        var subscription = broker.SubscribeFrom(parsed);
        Assert.Single(subscription.ReplayEvents);
        Assert.Equal(1, subscription.ReplayEvents[0].StreamId);
    }

    [Fact]
    public void Duplicado_expulsado_del_buffer_no_se_republica()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 2);
        Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(1, "alert", new { n = 1 }));
        Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(2, "alert", new { n = 2 }));
        Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(3, "alert", new { n = 3 }));

        Assert.Equal(ExternalPublishResult.Duplicate, broker.PublishExternal(1, "alert", new { n = 1 }));
        Assert.Equal(3, broker.LastAcceptedExternalOffset);
    }

    [Fact]
    public void Offset_menor_al_ultimo_aceptado_no_se_transmite()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        Assert.Equal(ExternalPublishResult.Accepted, broker.PublishExternal(50, "alert", new { n = 1 }));
        Assert.Equal(ExternalPublishResult.OutOfOrder, broker.PublishExternal(49, "alert", new { n = 2 }));

        Assert.True(subscription.LiveReader.TryRead(out var only));
        Assert.Equal(50, only.StreamId);
        Assert.False(subscription.LiveReader.TryRead(out _));
        Assert.Equal(1, broker.OutOfOrderEvents);
    }

    [Fact]
    public void Publicaciones_concurrentes_mantienen_offsets_monotonicos_en_entrega()
    {
        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 200, replayBufferSize: 200);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        broker.PublishExternal(5, "alert", new { n = 5 });

        Parallel.For(6, 26, offset =>
            broker.PublishExternal(offset, "alert", new { n = offset }));

        var delivered = new List<long>();
        while (subscription.LiveReader.TryRead(out var evt))
            delivered.Add(evt.StreamId);

        Assert.NotEmpty(delivered);
        for (var i = 1; i < delivered.Count; i++)
            Assert.True(delivered[i] > delivered[i - 1]);
    }

    [Fact]
    public void El_tamano_del_replay_no_define_la_deduplicacion()
    {
        var broker = new FleetSseBroker(TimeProvider.System, replayBufferSize: 1);
        broker.PublishExternal(10, "alert", new { n = 10 });
        broker.PublishExternal(11, "alert", new { n = 11 });

        Assert.Equal(ExternalPublishResult.Duplicate, broker.PublishExternal(10, "alert", new { n = 10 }));
        Assert.Equal(11, broker.LastAcceptedExternalOffset);
    }
}
