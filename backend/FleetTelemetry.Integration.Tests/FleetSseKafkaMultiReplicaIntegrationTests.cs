using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

// FT-005: integración vía KafkaManualAssignmentPump real (mismo camino que el hosted service).
[Collection(KafkaIntegrationCollection.Name)]
public class FleetSseKafkaMultiReplicaIntegrationTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public FleetSseKafkaMultiReplicaIntegrationTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task Dos_replicas_reales_reciben_los_mismos_offsets_futuros()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005");
        await _kafka.CreateTopicAsync(topic);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 40);

        await using var replicaA = StartReplica(_kafka.BootstrapServers, topic, "api-1");
        await using var replicaB = StartReplica(_kafka.BootstrapServers, topic, "api-2");

        await replicaA.WaitReadyAsync();
        await replicaB.WaitReadyAsync();
        Assert.Equal(replicaA.Coordinator.BaselineOffset, replicaB.Coordinator.BaselineOffset);

        var admissionA = replicaA.Coordinator.TryOpenStream(new SseLastEventId.Missing());
        var admissionB = replicaB.Coordinator.TryOpenStream(new SseLastEventId.Missing());
        Assert.True(admissionA.Admitted);
        Assert.Equal("initial-snapshot", admissionA.Subscription!.ResetReason);

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 5);
        var start = replicaA.Coordinator.BaselineOffset!.Value + 1;
        await replicaA.WaitUntilOffsetAsync(start + 4);
        await replicaB.WaitUntilOffsetAsync(start + 4);

        var idsA = DrainLiveIds(admissionA.Subscription!);
        var idsB = DrainLiveIds(admissionB.Subscription!);
        Assert.Equal(5, idsA.Count);
        Assert.Equal(idsA, idsB);
        Assert.Equal(idsA.OrderBy(x => x).ToArray(), idsA);
    }

    [Fact]
    public async Task Fallo_transitorio_y_topico_quieto_vuelve_a_Ready()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-quiet");
        await _kafka.CreateTopicAsync(topic);

        var broker = new IntegrationFailingBroker();
        await using var replica = StartReplica(_kafka.BootstrapServers, topic, "api-quiet", broker);
        await replica.WaitReadyAsync();

        var failOffset = replica.Coordinator.BaselineOffset!.Value + 1;
        broker.Deny(failOffset);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 1);

        await WaitUntilAsync(
            () => replica.Coordinator.State == RealtimeStreamState.Recovering,
            TimeSpan.FromSeconds(20));
        Assert.False(replica.Coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);

        broker.Allow(failOffset);
        await WaitUntilAsync(() => replica.Coordinator.IsReady, TimeSpan.FromSeconds(20));
        Assert.True(replica.Coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
    }

    [Fact]
    public async Task Reinicio_de_sesion_continua_desde_LastProcessed_mas_uno()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-session");
        await _kafka.CreateTopicAsync(topic);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 20);

        var broker = new FleetSseBroker(TimeProvider.System, channelCapacity: 100, replayBufferSize: 200);
        var coordinator = new RealtimeStreamCoordinator(broker);

        await using (var first = StartReplica(
                         _kafka.BootstrapServers, topic, "api-session", broker, coordinator))
        {
            await first.WaitReadyAsync();
            await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 6);
            var target = first.Coordinator.BaselineOffset!.Value + 6;
            await first.WaitUntilOffsetAsync(target);
            Assert.Equal(target, broker.LastProcessedExternalOffset);
        }

        var lastProcessed = broker.LastProcessedExternalOffset;
        coordinator.EnterRecovering("session-restart");

        // Misma réplica lógica: nuevo consumidor Assign en LastProcessed+1 (vía pump real).
        await using var resumed = StartReplica(
            _kafka.BootstrapServers,
            topic,
            "api-session-resume",
            broker,
            coordinator,
            startInRecovery: true);
        await resumed.WaitReadyAsync();
        Assert.Equal(lastProcessed, broker.LastProcessedExternalOffset);

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 3);
        await resumed.WaitUntilOffsetAsync(lastProcessed + 3);
        Assert.Equal(lastProcessed + 3, broker.LastProcessedExternalOffset);
    }

    [Fact]
    public async Task Topic_recreado_resume_mayor_que_High_obliga_nueva_baseline()
    {
        var oldTopic = _kafka.NewTopicName("fleet-realtime-ft005-old");
        await _kafka.CreateTopicAsync(oldTopic);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, oldTopic, 30);

        await using var first = StartReplica(_kafka.BootstrapServers, oldTopic, "api-old");
        await first.WaitReadyAsync();
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, oldTopic, 5);
        await first.WaitUntilOffsetAsync(first.Coordinator.BaselineOffset!.Value + 5);
        var lastProcessed = first.Broker.LastProcessedExternalOffset;

        var recreated = _kafka.NewTopicName("fleet-realtime-ft005-new");
        await _kafka.CreateTopicAsync(recreated);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, recreated, 4);

        // Misma réplica lógica: LastProcessed alto frente a High bajo del topic nuevo.
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.EstablishBaseline(lastProcessed);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterRecovering("fatal-before-recreate");

        var factory = new ConfluentRealtimeKafkaConsumerFactory(_kafka.BootstrapServers, recreated);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pump = new KafkaManualAssignmentPump(
            broker,
            coordinator,
            factory,
            recreated,
            $"fleet-realtime-sse-recreate-{Guid.NewGuid():N}",
            NullLogger.Instance,
            pollTimeout: TimeSpan.FromMilliseconds(200),
            startInRecovery: true);

        var runTask = pump.RunAsync(cts.Token);
        await WaitUntilAsync(() => coordinator.IsReady, TimeSpan.FromSeconds(20));

        Assert.Equal(3, broker.LastProcessedExternalOffset);
        Assert.Equal(3, coordinator.BaselineOffset);
        var admission = coordinator.TryOpenStream(new SseLastEventId.Missing());
        Assert.True(admission.Admitted);
        Assert.Equal("initial-snapshot", admission.Subscription!.ResetReason);
        Assert.Equal("3", admission.Subscription.LatestEventId);

        cts.Cancel();
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task No_se_admite_SSE_durante_Recovering()
    {
        var topic = _kafka.NewTopicName("fleet-realtime-ft005-reco");
        await _kafka.CreateTopicAsync(topic);

        var broker = new IntegrationFailingBroker();
        await using var replica = StartReplica(_kafka.BootstrapServers, topic, "api-reco", broker);
        await replica.WaitReadyAsync();

        broker.Deny(replica.Coordinator.BaselineOffset!.Value + 1);
        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 1);

        await WaitUntilAsync(
            () => replica.Coordinator.State == RealtimeStreamState.Recovering,
            TimeSpan.FromSeconds(20));

        var denied = replica.Coordinator.TryOpenStream(new SseLastEventId.Missing());
        Assert.False(denied.Admitted);
        Assert.Equal(RealtimeStreamState.Recovering, denied.State);
        Assert.Equal(0, replica.Broker.SubscriberCount);
    }

    [Fact]
    public async Task Api_1_y_api_2_usan_grupos_de_consumo_diferentes()
    {
        var serviceA = CreateHostedService("api-1");
        var serviceB = CreateHostedService("api-2");
        Assert.Equal("fleet-realtime-sse-api-1", serviceA.ConsumerGroupId);
        Assert.Equal("fleet-realtime-sse-api-2", serviceB.ConsumerGroupId);
        await Task.CompletedTask;
    }

    [Fact]
    public void KafkaPush_rechaza_topic_con_mas_de_una_particion()
    {
        Assert.Throws<RealtimeTopicPartitionCountException>(() =>
            RealtimeTopicValidator.EnsureSinglePartition(
                _kafka.BootstrapServers,
                "__consumer_offsets",
                required: true));
    }

    private PumpReplica StartReplica(
        string bootstrapServers,
        string topic,
        string instanceId,
        FleetSseBroker? broker = null,
        RealtimeStreamCoordinator? coordinator = null,
        bool startInRecovery = false)
    {
        broker ??= new FleetSseBroker(TimeProvider.System, channelCapacity: 100, replayBufferSize: 200);
        coordinator ??= new RealtimeStreamCoordinator(broker);
        var factory = new ConfluentRealtimeKafkaConsumerFactory(bootstrapServers, topic);
        var groupId = $"fleet-realtime-sse-{instanceId}-{Guid.NewGuid():N}";
        var pump = new KafkaManualAssignmentPump(
            broker,
            coordinator,
            factory,
            topic,
            groupId,
            NullLogger.Instance,
            pollTimeout: TimeSpan.FromMilliseconds(200),
            startInRecovery: startInRecovery);

        var cts = new CancellationTokenSource();
        var task = pump.RunAsync(cts.Token);
        return new PumpReplica(broker, coordinator, cts, task);
    }

    private FleetSseKafkaPushHostedService CreateHostedService(string instanceId)
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        return new(
            broker,
            Options.Create(new KafkaOptions
            {
                BootstrapServers = _kafka.BootstrapServers,
                RealtimeConsumerGroupBase = "fleet-realtime-sse"
            }),
            Options.Create(new SseOptions { InstanceId = instanceId }),
            new RealtimeStreamCoordinator(broker),
            NullLogger<FleetSseKafkaPushHostedService>.Instance);
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

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        Assert.True(condition(), "Timeout esperando condición");
    }

    private sealed class PumpReplica : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _runTask;

        public PumpReplica(
            FleetSseBroker broker,
            IRealtimeStreamCoordinator coordinator,
            CancellationTokenSource cts,
            Task runTask)
        {
            Broker = broker;
            Coordinator = coordinator;
            _cts = cts;
            _runTask = runTask;
        }

        public FleetSseBroker Broker { get; }
        public IRealtimeStreamCoordinator Coordinator { get; }

        public async Task WaitReadyAsync()
        {
            await WaitUntilAsync(() => Coordinator.IsReady, TimeSpan.FromSeconds(30));
        }

        public async Task WaitUntilOffsetAsync(long minOffset)
        {
            await WaitUntilAsync(
                () => Broker.LastProcessedExternalOffset >= minOffset,
                TimeSpan.FromSeconds(30));
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }

    private sealed class IntegrationFailingBroker : FleetSseBroker
    {
        private readonly HashSet<long> _denied = [];

        public IntegrationFailingBroker() : base(TimeProvider.System)
        {
        }

        public void Deny(long offset) => _denied.Add(offset);

        public void Allow(long offset) => _denied.Remove(offset);

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
