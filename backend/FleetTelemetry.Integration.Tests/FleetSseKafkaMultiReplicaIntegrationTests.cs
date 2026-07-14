using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

// FT-005: fan-out Kafka entre réplicas con Assign manual (sin Subscribe/rebalance).
[Collection(KafkaIntegrationCollection.Name)]
public class FleetSseKafkaMultiReplicaIntegrationTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public FleetSseKafkaMultiReplicaIntegrationTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task Dos_replicas_con_Assign_manual_reciben_los_mismos_offsets_futuros()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005");
        await _kafka.CreateTopicAsync(topic);

        var brokerA = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);
        var brokerB = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);
        brokerA.EstablishBaseline(-1);
        brokerB.EstablishBaseline(-1);

        var subA = brokerA.SubscribeFrom(new SseLastEventId.Missing());
        var subB = brokerB.SubscribeFrom(new SseLastEventId.Missing());

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 5);

        await RunAssignedPushLoopAsync(_kafka.BootstrapServers, topic, "api-1", brokerA, assignOffset: 0, expectedLast: 4);
        await RunAssignedPushLoopAsync(_kafka.BootstrapServers, topic, "api-2", brokerB, assignOffset: 0, expectedLast: 4);

        var idsA = DrainLiveIds(subA);
        var idsB = DrainLiveIds(subB);

        Assert.Equal(5, idsA.Count);
        Assert.Equal(5, idsB.Count);
        Assert.Equal(idsA, idsB);
        Assert.Equal(idsA.OrderBy(id => id).ToArray(), idsA);
    }

    [Fact]
    public async Task Api_1_y_api_2_usan_grupos_de_consumo_diferentes()
    {
        var serviceA = CreateHostedService("api-1");
        var serviceB = CreateHostedService("api-2");

        Assert.Equal("fleet-realtime-sse-api-1", serviceA.ConsumerGroupId);
        Assert.Equal("fleet-realtime-sse-api-2", serviceB.ConsumerGroupId);
        Assert.NotEqual(serviceA.ConsumerGroupId, serviceB.ConsumerGroupId);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Reiniciar_replica_genera_replay_gap_explicito()
    {
        var restarted = new FleetSseBroker(TimeProvider.System, replayBufferSize: 100);
        var subscription = restarted.SubscribeFrom(new SseLastEventId.ValidCursor(3));

        Assert.Equal(SseReplayStatus.ReplayGap, subscription.ReplayStatus);
        Assert.Equal("instance-restarted", subscription.ResetReason);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Fallo_de_publicacion_no_consume_offset_siguiente()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-fail");
        await _kafka.CreateTopicAsync(topic);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 3);

        var broker = new IntegrationFailingBroker(1);
        broker.EstablishBaseline(-1);
        using var transport = CreateAssignedTransport(_kafka.BootstrapServers, topic, "api-fail", assignOffset: 0);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));

        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        Assert.Equal(0, broker.LastProcessedExternalOffset);

        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        Assert.NotNull(loop.PendingRecord);
        Assert.Equal(1, loop.PendingRecord!.Offset.Value);

        // Mientras 1 está pendiente no avanza a 2.
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        Assert.Equal(1, loop.PendingRecord!.Offset.Value);
        Assert.True(broker.LastProcessedExternalOffset < 2);

        broker.AllowOffset(1);
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        Assert.Null(loop.PendingRecord);
        Assert.Equal(1, broker.LastProcessedExternalOffset);

        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        Assert.Equal(2, broker.LastProcessedExternalOffset);
    }

    [Fact]
    public void KafkaPush_rechaza_topic_con_mas_de_una_particion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RealtimeTopicValidator.EnsureSinglePartition(
                _kafka.BootstrapServers,
                "__consumer_offsets",
                required: true));
    }

    [Fact]
    public async Task Arranque_en_High_con_initial_snapshot_cubre_historial()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-ready");
        await _kafka.CreateTopicAsync(topic);

        // Histórico previo al High.
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 100);

        var brokerA = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 200);
        var brokerB = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 200);
        var coordinatorA = new RealtimeStreamCoordinator(brokerA);
        var coordinatorB = new RealtimeStreamCoordinator(brokerB);

        using var sessionA = CreateAssignedSession(
            _kafka.BootstrapServers, topic, "api-ready-a", brokerA, coordinatorA);
        using var sessionB = CreateAssignedSession(
            _kafka.BootstrapServers, topic, "api-ready-b", brokerB, coordinatorB);

        Assert.Equal(RealtimeStreamState.Ready, coordinatorA.State);
        Assert.Equal(RealtimeStreamState.Ready, coordinatorB.State);
        Assert.Equal(99, coordinatorA.BaselineOffset);
        Assert.Equal(99, coordinatorB.BaselineOffset);
        Assert.Equal(99, sessionA.Broker.LastProcessedExternalOffset);
        Assert.Equal(99, sessionB.Broker.LastProcessedExternalOffset);

        var admissionA = coordinatorA.TryOpenStream(new SseLastEventId.Missing());
        var admissionB = coordinatorB.TryOpenStream(new SseLastEventId.Missing());
        Assert.True(admissionA.Admitted);
        Assert.Equal("initial-snapshot", admissionA.Subscription!.ResetReason);
        Assert.Equal("99", admissionA.Subscription.LatestEventId);
        Assert.Equal(admissionA.Subscription.CutoverId, admissionB.Subscription!.CutoverId);

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 3);
        await WaitUntilOffsetAsync(sessionA, 102);
        await WaitUntilOffsetAsync(sessionB, 102);

        Assert.Equal(sessionA.Broker.LastProcessedExternalOffset, sessionB.Broker.LastProcessedExternalOffset);

        var idsA = DrainLiveIds(admissionA.Subscription);
        var idsB = DrainLiveIds(admissionB.Subscription);
        Assert.Equal(new[] { 100L, 101L, 102L }, idsA);
        Assert.Equal(idsA, idsB);
    }

    [Fact]
    public async Task Recuperacion_continua_desde_LastProcessed_mas_uno()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-recover");
        await _kafka.CreateTopicAsync(topic);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 50);

        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 100, replayBufferSize: 200);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var session = CreateAssignedSession(
            _kafka.BootstrapServers, topic, "api-recover", broker, coordinator);

        Assert.Equal(49, broker.LastProcessedExternalOffset);

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 6);
        await WaitUntilOffsetAsync(session, 55);
        Assert.Equal(55, broker.LastProcessedExternalOffset);

        // Simula recuperación: abandonar pending y Assign en LastProcessed+1.
        session.Loop.AbandonPending();
        session.Transport.Assign(56);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 4);
        await WaitUntilOffsetAsync(session, 59);

        Assert.Equal(59, broker.LastProcessedExternalOffset);
        for (var offset = 50L; offset <= 59; offset++)
            Assert.False(broker.IsInvalidCommittedOffset(offset));
    }

    private FleetSseKafkaPushHostedService CreateHostedService(
        string instanceId,
        FleetSseBroker? broker = null,
        IRealtimeStreamCoordinator? coordinator = null)
    {
        broker ??= new FleetSseBroker(TimeProvider.System);
        coordinator ??= new RealtimeStreamCoordinator(broker);
        return new(
            broker,
            Options.Create(new KafkaOptions { RealtimeConsumerGroupBase = "fleet-realtime-sse" }),
            Options.Create(new SseOptions { InstanceId = instanceId }),
            coordinator,
            NullLogger<FleetSseKafkaPushHostedService>.Instance);
    }

    private async Task RunAssignedPushLoopAsync(
        string bootstrapServers,
        string topic,
        string instanceId,
        FleetSseBroker broker,
        long assignOffset,
        long expectedLast)
    {
        using var transport = CreateAssignedTransport(bootstrapServers, topic, instanceId, assignOffset);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (broker.LastAcceptedExternalOffset < expectedLast && DateTime.UtcNow < deadline)
            loop.RunOnce(transport, TimeSpan.FromMilliseconds(500));

        Assert.True(
            broker.LastAcceptedExternalOffset >= expectedLast,
            $"Replica {instanceId} no consumió hasta {expectedLast}.");
        await Task.CompletedTask;
    }

    private AssignedKafkaPushTransport CreateAssignedTransport(
        string bootstrapServers,
        string topic,
        string instanceId,
        long assignOffset)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"fleet-realtime-sse-{instanceId}-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();

        consumer.Assign([new TopicPartitionOffset(topic, 0, assignOffset)]);
        return new AssignedKafkaPushTransport(consumer, topic);
    }

    private AssignedSession CreateAssignedSession(
        string bootstrapServers,
        string topic,
        string instanceId,
        FleetSseBroker broker,
        IRealtimeStreamCoordinator coordinator)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"fleet-realtime-sse-{instanceId}-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Latest
        }).Build();

        var partition = new TopicPartition(topic, 0);
        var watermarks = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10));
        var high = watermarks.High.Value;
        var baseline = high - 1;
        broker.EstablishBaseline(baseline);
        consumer.Assign([new TopicPartitionOffset(partition, new Offset(high))]);
        coordinator.EnterReady(baseline);

        var transport = new AssignedKafkaPushTransport(consumer, topic);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));
        return new AssignedSession(coordinator, broker, transport, loop);
    }

    private async Task WaitUntilOffsetAsync(AssignedSession session, long minOffset)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (session.Broker.LastAcceptedExternalOffset < minOffset && DateTime.UtcNow < deadline)
        {
            var result = session.Loop.RunOnce(session.Transport, TimeSpan.FromMilliseconds(500));
            if (result == KafkaPushPollResult.FatalFailure)
            {
                session.Coordinator.EnterFaulted("Fatal Kafka consume error");
                break;
            }

            if (result == KafkaPushPollResult.TransientFailure
                && session.Coordinator.State == RealtimeStreamState.Ready)
            {
                session.Coordinator.EnterRecovering("transient");
            }

            if (result == KafkaPushPollResult.Completed
                && session.Coordinator.State == RealtimeStreamState.Recovering)
            {
                session.Coordinator.EnterReady();
            }
        }

        Assert.True(
            session.Broker.LastAcceptedExternalOffset >= minOffset,
            $"No se alcanzó offset {minOffset} (actual {session.Broker.LastAcceptedExternalOffset}).");
        await Task.CompletedTask;
    }

    private static async Task ProduceVehicleUpdatesAsync(string bootstrapServers, string topic, int count)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        for (var index = 0; index < count; index++)
        {
            var message = FleetRealtimeKafkaMessage.Serialize(new FleetRealtimeKafkaMessage
            {
                SchemaVersion = FleetRealtimeKafkaMessage.CurrentSchemaVersion,
                EventType = FleetRealtimeEventTypes.VehicleUpdate,
                Payload = System.Text.Json.JsonDocument.Parse(
                    $$"""{"vehicleId":"VH-{{index:D3}}","name":"VH-{{index:D3}}","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"}""").RootElement,
                OccurredAt = DateTimeOffset.UtcNow,
                VehicleId = $"VH-{index:D3}"
            });

            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = $"VH-{index:D3}",
                Value = message
            });
        }

        producer.Flush(TimeSpan.FromSeconds(5));
    }

    private static List<long> DrainLiveIds(Application.DTOs.SseSubscription subscription)
    {
        var ids = new List<long>();
        while (subscription.LiveReader.TryRead(out var evt))
        {
            if (evt.StreamId >= 0)
                ids.Add(evt.StreamId);
        }

        return ids;
    }

    private sealed class AssignedSession : IDisposable
    {
        public AssignedSession(
            IRealtimeStreamCoordinator coordinator,
            FleetSseBroker broker,
            AssignedKafkaPushTransport transport,
            FleetRealtimeKafkaPushLoop loop)
        {
            Coordinator = coordinator;
            Broker = broker;
            Transport = transport;
            Loop = loop;
        }

        public IRealtimeStreamCoordinator Coordinator { get; }
        public FleetSseBroker Broker { get; }
        public AssignedKafkaPushTransport Transport { get; }
        public FleetRealtimeKafkaPushLoop Loop { get; }

        public void Dispose() => Transport.Dispose();
    }

    private sealed class AssignedKafkaPushTransport : IRealtimeKafkaPushTransport, IDisposable
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly string _topic;

        public AssignedKafkaPushTransport(IConsumer<string, string> consumer, string topic)
        {
            _consumer = consumer;
            _topic = topic;
        }

        public ConsumeResult<string, string>? Consume(TimeSpan timeout) =>
            _consumer.Consume(timeout);

        public void Assign(long offset) =>
            _consumer.Assign([new TopicPartitionOffset(_topic, 0, offset)]);

        public void Dispose() => _consumer.Dispose();
    }

    private sealed class IntegrationFailingBroker : FleetSseBroker
    {
        private readonly HashSet<long> _denied;

        public IntegrationFailingBroker(long deniedOffset) : base(TimeProvider.System) =>
            _denied = [deniedOffset];

        public void AllowOffset(long offset) => _denied.Remove(offset);

        public override ExternalPublishResult PublishExternal(
            long streamId,
            string eventType,
            object data,
            DateTimeOffset? timestamp = null)
        {
            if (_denied.Contains(streamId))
                throw new InvalidOperationException($"Simulated publish failure at offset {streamId}");

            return base.PublishExternal(streamId, eventType, data, timestamp);
        }
    }
}
