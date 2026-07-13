using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

// FT-005: fan-out Kafka entre réplicas usando el loop real de push SSE.
[Collection(KafkaIntegrationCollection.Name)]
public class FleetSseKafkaMultiReplicaIntegrationTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public FleetSseKafkaMultiReplicaIntegrationTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task Dos_instancias_logicas_reciben_todos_los_eventos_con_mismo_id_y_orden()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005");
        await _kafka.CreateTopicAsync(topic);

        var brokerA = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);
        var brokerB = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);

        var subA = brokerA.SubscribeFrom(new SseLastEventId.Missing());
        var subB = brokerB.SubscribeFrom(new SseLastEventId.Missing());

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 5);

        await RunHostedPushLoopAsync(_kafka.BootstrapServers, topic, "api-1", brokerA, expectedMessages: 5);
        await RunHostedPushLoopAsync(_kafka.BootstrapServers, topic, "api-2", brokerB, expectedMessages: 5);

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
    public async Task Fallo_de_publicacion_no_permite_confirmar_offsets_posteriores()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-fail");
        await _kafka.CreateTopicAsync(topic);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 3);

        var broker = new IntegrationFailingBroker(1);
        var transport = CreateTransport(_kafka.BootstrapServers, topic, "api-fail");
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));

        transport.AssignBeginning();
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));

        Assert.Equal([0], transport.CommittedOffsets);
        Assert.Equal(1, loop.BlockedOffset);

        broker.AllowOffset(1);
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));
        loop.RunOnce(transport, TimeSpan.FromSeconds(5));

        Assert.Equal([0, 1, 2], transport.CommittedOffsets);
        Assert.Null(loop.BlockedOffset);
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

    private FleetSseKafkaPushHostedService CreateHostedService(string instanceId) =>
        new(
            new FleetSseBroker(TimeProvider.System),
            Options.Create(new KafkaOptions { RealtimeConsumerGroupBase = "fleet-realtime-sse" }),
            Options.Create(new SseOptions { InstanceId = instanceId }),
            new FleetKafkaPushReadiness(),
            NullLogger<FleetSseKafkaPushHostedService>.Instance);

    private async Task RunHostedPushLoopAsync(
        string bootstrapServers,
        string topic,
        string instanceId,
        FleetSseBroker broker,
        int expectedMessages)
    {
        using var transport = CreateTransport(bootstrapServers, topic, instanceId);
        var loop = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(broker));
        transport.AssignBeginning();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (broker.LastAcceptedExternalOffset < expectedMessages - 1 && DateTime.UtcNow < deadline)
            loop.RunOnce(transport, TimeSpan.FromMilliseconds(500));

        Assert.True(
            broker.LastAcceptedExternalOffset >= expectedMessages - 1,
            $"Replica {instanceId} no consumió {expectedMessages} mensajes.");
        await Task.CompletedTask;
    }

    private TestKafkaPushTransport CreateTransport(string bootstrapServers, string topic, string instanceId)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"fleet-realtime-sse-{instanceId}-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();

        return new TestKafkaPushTransport(consumer, topic);
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
            ids.Add(evt.StreamId);

        return ids;
    }

    private sealed class TestKafkaPushTransport : IRealtimeKafkaPushTransport, IDisposable
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly string _topic;

        public TestKafkaPushTransport(IConsumer<string, string> consumer, string topic)
        {
            _consumer = consumer;
            _topic = topic;
        }

        public List<long> CommittedOffsets { get; } = [];

        public void AssignBeginning() =>
            _consumer.Assign(new TopicPartitionOffset(_topic, 0, Offset.Beginning));

        public ConsumeResult<string, string>? Consume(TimeSpan timeout) =>
            _consumer.Consume(timeout);

        public void Commit(ConsumeResult<string, string> result)
        {
            _consumer.Commit(result);
            CommittedOffsets.Add(result.Offset.Value);
        }

        public void Seek(long offset) =>
            _consumer.Seek(new TopicPartitionOffset(_topic, 0, offset));

        public void Dispose() => _consumer.Dispose();
    }

    [Fact]
    public async Task Produccion_Latest_no_admite_SSE_antes_de_Ready_y_no_pierde_eventos()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-ready");
        await _kafka.CreateTopicAsync(topic);

        var readinessA = new FleetKafkaPushReadiness();
        var readinessB = new FleetKafkaPushReadiness();
        var brokerA = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);
        var brokerB = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);

        Assert.False(readinessA.IsReady);
        Assert.False(readinessB.IsReady);

        // Equivalente al gate de EventsController antes de Ready.
        Assert.Equal(FleetKafkaPushReadinessState.Starting, readinessA.State);

        using var transportA = CreateProductionTransport(
            _kafka.BootstrapServers, topic, "api-ready-a", readinessA);
        using var transportB = CreateProductionTransport(
            _kafka.BootstrapServers, topic, "api-ready-b", readinessB);

        var loopA = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(brokerA));
        var loopB = new FleetRealtimeKafkaPushLoop(new RealtimeKafkaPushProcessor(brokerB));

        // Evento producido durante el arranque (antes de Ready).
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 1);

        await WaitUntilReadyAsync(readinessA, loopA, transportA);
        await WaitUntilReadyAsync(readinessB, loopB, transportB);

        Assert.True(readinessA.IsReady);
        Assert.True(readinessB.IsReady);
        Assert.Equal(readinessA.InitialPositionOffset, readinessB.InitialPositionOffset);

        var startOffset = readinessA.InitialPositionOffset!.Value;

        // Eventos futuros tras Ready: ambas réplicas convergen al mismo límite.
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 3);
        await WaitUntilOffsetAsync(brokerA, loopA, transportA, minOffset: startOffset + 2);
        await WaitUntilOffsetAsync(brokerB, loopB, transportB, minOffset: startOffset + 2);

        Assert.Equal(brokerA.LastAcceptedExternalOffset, brokerB.LastAcceptedExternalOffset);

        var cutoverBefore = brokerA.LastAcceptedExternalOffset;
        var subA = brokerA.SubscribeFrom(new SseLastEventId.Missing());
        var subB = brokerB.SubscribeFrom(new SseLastEventId.Missing());

        Assert.Equal("initial-snapshot", subA.ResetReason);
        Assert.Equal("initial-snapshot", subB.ResetReason);
        Assert.Equal(cutoverBefore, subA.CutoverId);
        Assert.Equal(subA.CutoverId, subB.CutoverId);
        Assert.Equal(subA.LatestEventId, subB.LatestEventId);

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 2);
        var nextOffset = cutoverBefore + 1;
        await WaitUntilOffsetAsync(brokerA, loopA, transportA, minOffset: nextOffset + 1);
        await WaitUntilOffsetAsync(brokerB, loopB, transportB, minOffset: nextOffset + 1);

        var idsA = DrainLiveIds(subA);
        var idsB = DrainLiveIds(subB);
        Assert.Equal(new[] { nextOffset, nextOffset + 1 }, idsA);
        Assert.Equal(idsA, idsB);
    }

    private async Task WaitUntilReadyAsync(
        IFleetKafkaPushReadiness readiness,
        FleetRealtimeKafkaPushLoop loop,
        ProductionKafkaPushTransport transport)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!readiness.IsReady && DateTime.UtcNow < deadline)
            loop.RunOnce(transport, TimeSpan.FromMilliseconds(500));

        Assert.True(readiness.IsReady, "Kafka push no alcanzó Ready a tiempo.");
        await Task.CompletedTask;
    }

    private async Task WaitUntilOffsetAsync(
        FleetSseBroker broker,
        FleetRealtimeKafkaPushLoop loop,
        IRealtimeKafkaPushTransport transport,
        long minOffset)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (broker.LastAcceptedExternalOffset < minOffset && DateTime.UtcNow < deadline)
            loop.RunOnce(transport, TimeSpan.FromMilliseconds(500));

        Assert.True(
            broker.LastAcceptedExternalOffset >= minOffset,
            $"No se alcanzó offset {minOffset} (actual {broker.LastAcceptedExternalOffset}).");
        await Task.CompletedTask;
    }

    private ProductionKafkaPushTransport CreateProductionTransport(
        string bootstrapServers,
        string topic,
        string instanceId,
        IFleetKafkaPushReadiness readiness)
    {
        var groupId = $"fleet-realtime-sse-{instanceId}-{Guid.NewGuid():N}";
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = false
        })
        .SetPartitionsAssignedHandler((c, partitions) =>
        {
            readiness.MarkAssigned();
            Assert.Single(partitions);
            var partition = partitions[0];
            var watermarks = c.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10));
            readiness.EstablishInitialPosition(watermarks.High.Value);
            readiness.MarkReady();
            return new[] { new TopicPartitionOffset(partition, watermarks.High) };
        })
        .Build();

        consumer.Subscribe(topic);
        return new ProductionKafkaPushTransport(consumer, topic);
    }

    private sealed class ProductionKafkaPushTransport : IRealtimeKafkaPushTransport, IDisposable
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly string _topic;

        public ProductionKafkaPushTransport(IConsumer<string, string> consumer, string topic)
        {
            _consumer = consumer;
            _topic = topic;
        }

        public ConsumeResult<string, string>? Consume(TimeSpan timeout) =>
            _consumer.Consume(timeout);

        public void Commit(ConsumeResult<string, string> result) =>
            _consumer.Commit(result);

        public void Seek(long offset) =>
            _consumer.Seek(new TopicPartitionOffset(_topic, 0, offset));

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
