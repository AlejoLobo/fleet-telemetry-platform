using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

public class KafkaResumePositionTests
{
    [Fact]
    public void Resume_dentro_del_rango_continua_desde_LastProcessed_mas_uno()
    {
        var plan = KafkaResumePosition.Resolve(lastProcessedExternalOffset: 55, low: 0, high: 100);
        Assert.Equal(56, plan.AssignOffset);
        Assert.Null(plan.NewBaseline);
    }

    [Fact]
    public void Resume_menor_que_Low_crea_nueva_baseline()
    {
        var plan = KafkaResumePosition.Resolve(lastProcessedExternalOffset: 55, low: 200, high: 250);
        Assert.Equal(250, plan.AssignOffset);
        Assert.Equal(249, plan.NewBaseline);
    }

    [Fact]
    public void Resume_mayor_que_High_crea_nueva_baseline()
    {
        var plan = KafkaResumePosition.Resolve(lastProcessedExternalOffset: 99, low: 0, high: 10);
        Assert.Equal(10, plan.AssignOffset);
        Assert.Equal(9, plan.NewBaseline);
    }
}

public class KafkaAssignmentMaterializerTests
{
    [Fact]
    public void Materializacion_expira_sin_confirmacion()
    {
        var tp = new TopicPartition("fleet.realtime", 0);
        var assigned = new Dictionary<TopicPartition, long> { [tp] = 10 };
        var assignment = new List<TopicPartition>();

        var ex = Assert.Throws<RealtimeKafkaAssignmentMaterializationException>(() =>
            KafkaAssignmentMaterializer.Materialize(
                consume: _ => new ConsumeResult<string, string>
                {
                    Topic = tp.Topic,
                    Partition = tp.Partition,
                    Offset = 0
                },
                getAssignment: () => assignment,
                getPosition: _ => Offset.Unset,
                assignedOffsets: assigned,
                timeout: TimeSpan.FromMilliseconds(80),
                topic: tp.Topic));

        Assert.Equal("fleet.realtime", ex.Topic);
        Assert.Equal(0, ex.Partition);
        Assert.Equal(10, ex.TargetOffset);
    }

    [Fact]
    public void Materializacion_tip_confirmado_sin_prefetch()
    {
        var tp = new TopicPartition("fleet.realtime", 0);
        var assigned = new Dictionary<TopicPartition, long> { [tp] = 10 };
        var assignment = new List<TopicPartition> { tp };

        var prefetch = KafkaAssignmentMaterializer.Materialize(
            consume: _ => null,
            getAssignment: () => assignment,
            getPosition: _ => new Offset(10),
            assignedOffsets: assigned,
            timeout: TimeSpan.FromSeconds(1),
            topic: tp.Topic);

        Assert.Null(prefetch);
    }

    [Fact]
    public void Materializacion_tip_con_Assignment_y_Position_Unset_es_valido()
    {
        var tp = new TopicPartition("fleet.realtime", 0);
        var assigned = new Dictionary<TopicPartition, long> { [tp] = 10 };

        var prefetch = KafkaAssignmentMaterializer.Materialize(
            consume: _ => null,
            getAssignment: () => [tp],
            getPosition: _ => Offset.Unset,
            assignedOffsets: assigned,
            timeout: TimeSpan.FromSeconds(1),
            topic: tp.Topic);

        Assert.Null(prefetch);
    }

    [Fact]
    public void Materializacion_null_sin_Assignment_no_confirma_tip()
    {
        var tp = new TopicPartition("fleet.realtime", 0);
        var assigned = new Dictionary<TopicPartition, long> { [tp] = 10 };

        Assert.Throws<RealtimeKafkaAssignmentMaterializationException>(() =>
            KafkaAssignmentMaterializer.Materialize(
                consume: _ => null,
                getAssignment: () => Array.Empty<TopicPartition>(),
                getPosition: _ => Offset.Unset,
                assignedOffsets: assigned,
                timeout: TimeSpan.FromMilliseconds(80),
                topic: tp.Topic));
    }

    [Fact]
    public void Materializacion_registro_en_posicion_correcta_se_prefetchea()
    {
        var tp = new TopicPartition("fleet.realtime", 0);
        var assigned = new Dictionary<TopicPartition, long> { [tp] = 10 };
        var record = new ConsumeResult<string, string>
        {
            Topic = tp.Topic,
            Partition = tp.Partition,
            Offset = 10
        };

        var prefetch = KafkaAssignmentMaterializer.Materialize(
            consume: _ => record,
            getAssignment: () => [tp],
            getPosition: _ => new Offset(10),
            assignedOffsets: assigned,
            timeout: TimeSpan.FromSeconds(1),
            topic: tp.Topic);

        Assert.Same(record, prefetch);
    }
}

public class KafkaPushPollStateMachineTests
{
    [Fact]
    public void Consume_transitorio_seguido_de_Idle_vuelve_a_Ready()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(0);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.TransientFailure, null);
        Assert.Equal(RealtimeStreamState.Recovering, coordinator.State);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.Idle, null);
        Assert.True(coordinator.IsReady);
    }

    [Fact]
    public void Consume_transitorio_seguido_de_Completed_vuelve_a_Ready()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(0);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.TransientFailure, null);
        Assert.Equal(RealtimeStreamState.Recovering, coordinator.State);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.Completed, null);
        Assert.True(coordinator.IsReady);
    }

    [Fact]
    public void Topico_sin_eventos_no_deja_la_replica_en_Recovering()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.Idle, baselineForReady: 4);
        Assert.Equal(RealtimeStreamState.Ready, coordinator.State);
        Assert.Equal(4, coordinator.BaselineOffset);
        Assert.NotEqual(RealtimeStreamState.Recovering, coordinator.State);
    }

    [Fact]
    public void TransientFailure_repetido_no_habilita_Ready()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(0);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.TransientFailure, null);
        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.TransientFailure, null);
        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.TransientFailure, null);

        Assert.Equal(RealtimeStreamState.Recovering, coordinator.State);
        Assert.False(coordinator.IsReady);
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
    }

    [Fact]
    public void PendingRecord_no_puede_producir_Idle()
    {
        var broker = new AlwaysFailingBroker();
        var transport = new QueueTransport();
        var loop = new FleetRealtimeKafkaPushLoop(
            new RealtimeKafkaPushProcessor(broker),
            delayAsync: static (_, _) => Task.CompletedTask,
            backoff: TimeSpan.Zero);

        transport.Enqueue(ValidConsume(50));
        Assert.Equal(KafkaPushPollResult.TransientFailure, loop.RunOnce(transport, TimeSpan.FromMilliseconds(10)));
        Assert.NotNull(loop.PendingRecord);

        var second = loop.RunOnce(transport, TimeSpan.FromMilliseconds(10));
        Assert.NotEqual(KafkaPushPollResult.Idle, second);
        Assert.Equal(KafkaPushPollResult.TransientFailure, second);
        Assert.NotNull(loop.PendingRecord);
    }

    [Fact]
    public void SSE_vuelve_a_admitirse_despues_de_poll_Idle_saludable()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(0);
        Assert.True(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.TransientFailure, null);
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.Idle, null);
        Assert.True(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
    }

    [Fact]
    public void Assign_no_habilita_Ready_hasta_poll_saludable()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.TransientFailure, 10);
        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);
        Assert.False(coordinator.IsReady);

        KafkaPushPollStateMachine.Apply(coordinator, KafkaPushPollResult.Idle, 10);
        Assert.True(coordinator.IsReady);
        Assert.Equal(10, coordinator.BaselineOffset);
    }

    [Fact]
    public void FatalFailure_pide_recrear_consumidor()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(0);
        coordinator.TryOpenStream(new SseLastEventId.Missing());

        var transition = KafkaPushPollStateMachine.Apply(
            coordinator, KafkaPushPollResult.FatalFailure, null);
        Assert.True(transition.RecreateConsumer);
        Assert.Equal(RealtimeStreamState.Recovering, coordinator.State);
        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public void Recuperacion_outside_range_aplica_ResetToBaseline()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.EstablishBaseline(49);
        broker.PublishExternal(50, "alert", new { n = 50 });

        var belowLow = KafkaResumePosition.Resolve(broker.LastProcessedExternalOffset, low: 80, high: 100);
        Assert.Equal(100, belowLow.AssignOffset);
        broker.ResetToBaseline(belowLow.NewBaseline!.Value);
        Assert.Equal(99, broker.LastProcessedExternalOffset);

        broker.EstablishBaseline(105);
        var aboveHigh = KafkaResumePosition.Resolve(105, low: 0, high: 3);
        Assert.Equal(3, aboveHigh.AssignOffset);
        broker.ResetToBaseline(aboveHigh.NewBaseline!.Value);
        Assert.Equal(2, broker.LastProcessedExternalOffset);

        var missing = broker.SubscribeFrom(new SseLastEventId.Missing());
        Assert.Equal("initial-snapshot", missing.ResetReason);
        Assert.Equal("2", missing.LatestEventId);
    }

    private static ConsumeResult<string, string> ValidConsume(long offset) =>
        new()
        {
            Topic = "fleet.realtime",
            Partition = 0,
            Offset = offset,
            Message = new Message<string, string>
            {
                Value = FleetRealtimeKafkaMessage.Serialize(new FleetRealtimeKafkaMessage
                {
                    SchemaVersion = FleetRealtimeKafkaMessage.CurrentSchemaVersion,
                    EventType = FleetRealtimeEventTypes.VehicleUpdate,
                    Payload = System.Text.Json.JsonDocument.Parse(
                        """{"deviceId":"11111111-1111-1111-1111-111111111111","vehicleName":"VH-001","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"}""").RootElement,
                    OccurredAt = DateTimeOffset.UtcNow,
                    DeviceId = "11111111-1111-1111-1111-111111111111"
                })
            }
        };

    private sealed class QueueTransport : IRealtimeKafkaPushTransport
    {
        private readonly Queue<ConsumeResult<string, string>> _q = new();
        public void Enqueue(ConsumeResult<string, string> r) => _q.Enqueue(r);
        public ConsumeResult<string, string>? Consume(TimeSpan timeout) =>
            _q.Count == 0 ? null : _q.Dequeue();
    }

    private sealed class AlwaysFailingBroker : FleetSseBroker
    {
        public AlwaysFailingBroker() : base(TimeProvider.System) { }

        public override ExternalPublishResult PublishExternal(
            long streamId, string eventType, object data, DateTimeOffset? timestamp = null) =>
            throw new RealtimeKafkaTransientPublishException("fail", new InvalidOperationException());
    }
}

public class KafkaManualAssignmentPumpTests
{
    [Fact]
    public async Task Create_falla_temporalmente_y_luego_funciona()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var factory = new FailingCreateFactory(failCount: 2, onHealthyIdle: () => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory, delays);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady, TimeSpan.FromSeconds(5));
        Assert.Equal(3, factory.CreateCount);
        Assert.Equal(2, delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delays[1]);
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);
        Assert.True(coordinator.IsReady);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Create_no_admite_SSE_antes_del_poll_saludable()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var factory = new AssignThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => factory.Assigned, TimeSpan.FromSeconds(5));
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);

        factory.ReleaseIdle();
        await WaitUntilAsync(() => coordinator.IsReady, TimeSpan.FromSeconds(5));
        Assert.True(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task QueryWatermarkOffsets_falla_y_luego_funciona()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var factory = new FailWatermarksThenIdleFactory(onHealthyIdle: () => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady && factory.CreateCount >= 2, TimeSpan.FromSeconds(5));
        Assert.True(factory.Sessions[0].Disposed);
        Assert.True(factory.CreateCount >= 2);
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Assign_falla_y_luego_funciona()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var factory = new FailAssignThenIdleFactory(onHealthyIdle: () => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady && factory.CreateCount >= 2, TimeSpan.FromSeconds(5));
        Assert.True(factory.Sessions[0].Disposed);
        Assert.True(factory.AssignSuccessCount >= 1);
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Materializacion_expira_recrea_sesion()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var factory = new FailMaterializeThenIdleFactory(onHealthyIdle: () => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady && factory.CreateCount >= 2, TimeSpan.FromSeconds(5));
        Assert.True(factory.Sessions[0].Disposed);
        Assert.Null(factory.Sessions[0].PrefetchProbe);
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);
        Assert.True(coordinator.IsReady);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Cancelacion_durante_backoff_sigue_sin_Faulted()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var enteredBackoff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new AlwaysFailCreateFactory();

        var pump = new KafkaManualAssignmentPump(
            broker,
            coordinator,
            factory,
            topic: "fleet.realtime",
            groupId: "fleet-realtime-sse-test",
            delayAsync: async (_, ct) =>
            {
                enteredBackoff.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            },
            pollTimeout: TimeSpan.FromMilliseconds(20),
            watermarkTimeout: TimeSpan.FromMilliseconds(20));

        var run = pump.RunAsync(cts.Token);
        await enteredBackoff.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var createsBeforeCancel = factory.CreateCount;
        cts.Cancel();

        await AwaitCancelledAsync(run);
        Assert.Equal(createsBeforeCancel, factory.CreateCount);
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public async Task Fatal_inmediato_repetido_incrementa_backoff_200_400_800()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var factory = new AlwaysFatalFactory();

        var pump = CreatePump(broker, coordinator, factory, delays, onDelay: _ =>
        {
            if (delays.Count >= 3)
                cts.Cancel();
        });
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => delays.Count >= 3, TimeSpan.FromSeconds(5));
        await AwaitCancelledAsync(run);

        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(800), delays[2]);
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public async Task Prepare_exitoso_no_reinicia_backoff_sin_poll_saludable()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        // Fallo Create (racha=1) + Assign OK + Fatal sin Idle (racha=2) → backoff 400, no 200.
        var factory = new FailCreateThenFatalFactory();

        var pump = CreatePump(broker, coordinator, factory, delays, onDelay: _ =>
        {
            if (delays.Count >= 2)
                cts.Cancel();
        });
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => delays.Count >= 2, TimeSpan.FromSeconds(5));
        await AwaitCancelledAsync(run);

        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delays[1]);
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public async Task Idle_saludable_reinicia_la_racha()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var factory = new FailTwiceThenIdleThenFatalThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory, delays);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady && delays.Count >= 3, TimeSpan.FromSeconds(5));
        await AwaitCancelledAsync(run);

        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delays[1]);
        // Tras Idle saludable la Fatal siguiente debe volver a 200 ms.
        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[2]);
        Assert.True(coordinator.IsReady);
    }

    [Fact]
    public async Task Completed_saludable_reinicia_la_racha()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var factory = new FailTwiceThenCompletedThenFatalThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory, delays);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady && delays.Count >= 3, TimeSpan.FromSeconds(5));
        await AwaitCancelledAsync(run);

        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[2]);
        Assert.True(coordinator.IsReady);
    }

    [Fact]
    public async Task Fatal_despues_de_sesion_saludable_empieza_en_200ms()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var factory = new IdleThenFatalThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory, delays);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady && delays.Count >= 1, TimeSpan.FromSeconds(5));
        await AwaitCancelledAsync(run);

        Assert.Equal(TimeSpan.FromMilliseconds(200), delays[0]);
        Assert.True(coordinator.IsReady);
    }

    [Fact]
    public async Task Metadata_temporalmente_inaccesible_se_recupera()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var metadata = new SequenceMetadataSource(
            () => throw new RealtimeTopicMetadataUnavailableException("broker down"),
            () => 1);
        var factory = new AssignThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(
            broker,
            coordinator,
            factory,
            metadataSource: metadata,
            requireSinglePartition: true);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => factory.Assigned, TimeSpan.FromSeconds(5));
        Assert.True(metadata.CallCount >= 2);
        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);
        factory.ReleaseIdle();
        await WaitUntilAsync(() => coordinator.IsReady, TimeSpan.FromSeconds(5));
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);
        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Topic_aun_no_disponible_mantiene_Starting()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var metadata = new SequenceMetadataSource(
            () => throw new RealtimeTopicMetadataUnavailableException("topic missing"),
            () => throw new RealtimeTopicMetadataUnavailableException("topic missing"));
        var factory = new AssignThenIdleFactory(() => { });

        var pump = CreatePump(
            broker,
            coordinator,
            factory,
            metadataSource: metadata,
            requireSinglePartition: true,
            onDelay: _ =>
            {
                if (metadata.CallCount >= 2)
                    cts.Cancel();
            });
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => metadata.CallCount >= 2, TimeSpan.FromSeconds(5));
        await AwaitCancelledAsync(run);

        Assert.Equal(0, factory.CreateCount);
        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public async Task Metadata_transitoria_no_admite_SSE()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new SequenceMetadataSource(
            () => throw new RealtimeTopicMetadataUnavailableException("timeout"));
        var factory = new AssignThenIdleFactory(() => { });

        var pump = new KafkaManualAssignmentPump(
            broker,
            coordinator,
            factory,
            topic: "fleet.realtime",
            groupId: "fleet-realtime-sse-test",
            delayAsync: async (_, ct) =>
            {
                blocked.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            },
            pollTimeout: TimeSpan.FromMilliseconds(20),
            watermarkTimeout: TimeSpan.FromMilliseconds(20),
            metadataSource: metadata,
            requireSinglePartition: true);

        var run = pump.RunAsync(cts.Token);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);
        Assert.Equal(0, factory.CreateCount);

        cts.Cancel();
        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Particiones_distintas_de_uno_marca_Faulted()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        var metadata = new SequenceMetadataSource(() => 3);
        var factory = new AssignThenIdleFactory(() => { });

        var pump = CreatePump(
            broker,
            coordinator,
            factory,
            metadataSource: metadata,
            requireSinglePartition: true);

        await Assert.ThrowsAsync<RealtimeTopicPartitionCountException>(() => pump.RunAsync(CancellationToken.None));
        Assert.Equal(RealtimeStreamState.Faulted, coordinator.State);
        Assert.Equal(0, factory.CreateCount);
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
    }

    [Fact]
    public async Task Cancelacion_durante_retry_metadata_no_marca_Faulted()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var enteredBackoff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new SequenceMetadataSource(
            () => throw new RealtimeTopicMetadataUnavailableException("unavailable"));
        var factory = new AssignThenIdleFactory(() => { });

        var pump = new KafkaManualAssignmentPump(
            broker,
            coordinator,
            factory,
            topic: "fleet.realtime",
            groupId: "fleet-realtime-sse-test",
            delayAsync: async (_, ct) =>
            {
                enteredBackoff.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            },
            pollTimeout: TimeSpan.FromMilliseconds(20),
            watermarkTimeout: TimeSpan.FromMilliseconds(20),
            metadataSource: metadata,
            requireSinglePartition: true);

        var run = pump.RunAsync(cts.Token);
        await enteredBackoff.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await AwaitCancelledAsync(run);
        Assert.NotEqual(RealtimeStreamState.Faulted, coordinator.State);
        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task Validacion_exitosa_continua_hasta_poll_saludable()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var metadata = new SequenceMetadataSource(() => 1);
        var factory = new AssignThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(
            broker,
            coordinator,
            factory,
            metadataSource: metadata,
            requireSinglePartition: true);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => factory.Assigned, TimeSpan.FromSeconds(5));
        Assert.Equal(1, metadata.CallCount);
        Assert.False(coordinator.IsReady);
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);

        factory.ReleaseIdle();
        await WaitUntilAsync(() => coordinator.IsReady, TimeSpan.FromSeconds(5));
        Assert.True(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Consumidor_fatal_es_dispuesto()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var factory = new FatalThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => coordinator.IsReady, TimeSpan.FromSeconds(5));
        Assert.True(factory.Sessions[0].Disposed);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Recuperacion_crea_un_consumidor_nuevo()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var factory = new FatalThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(
            () => coordinator.IsReady && factory.CreateCount >= 2,
            TimeSpan.FromSeconds(5));
        Assert.NotSame(factory.Sessions[0], factory.Sessions[1]);
        Assert.True(factory.Sessions[0].Disposed);
        Assert.True(factory.CreateCount >= 2);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public async Task Assign_no_habilita_Ready_hasta_poll_saludable_via_pump()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        using var cts = new CancellationTokenSource();
        var factory = new AssignThenIdleFactory(() => cts.Cancel());

        var pump = CreatePump(broker, coordinator, factory);
        var run = pump.RunAsync(cts.Token);

        await WaitUntilAsync(() => factory.Assigned, TimeSpan.FromSeconds(5));
        Assert.False(coordinator.IsReady);

        factory.ReleaseIdle();
        await WaitUntilAsync(() => coordinator.IsReady, TimeSpan.FromSeconds(5));
        Assert.Equal(0, coordinator.BaselineOffset);

        await AwaitCancelledAsync(run);
    }

    [Fact]
    public void SessionBackoff_crece_hasta_cinco_segundos()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), KafkaManualAssignmentPump.ComputeSessionBackoff(1));
        Assert.Equal(TimeSpan.FromMilliseconds(400), KafkaManualAssignmentPump.ComputeSessionBackoff(2));
        Assert.Equal(TimeSpan.FromMilliseconds(800), KafkaManualAssignmentPump.ComputeSessionBackoff(3));
        Assert.Equal(TimeSpan.FromMilliseconds(1600), KafkaManualAssignmentPump.ComputeSessionBackoff(4));
        Assert.Equal(TimeSpan.FromSeconds(5), KafkaManualAssignmentPump.ComputeSessionBackoff(6));
    }

    private static KafkaManualAssignmentPump CreatePump(
        FleetSseBroker broker,
        IRealtimeStreamCoordinator coordinator,
        IRealtimeKafkaConsumerFactory factory,
        List<TimeSpan>? capturedDelays = null,
        Action<TimeSpan>? onDelay = null,
        IRealtimeTopicMetadataSource? metadataSource = null,
        bool requireSinglePartition = false) =>
        new(
            broker,
            coordinator,
            factory,
            topic: "fleet.realtime",
            groupId: "fleet-realtime-sse-test",
            delayAsync: (delay, ct) =>
            {
                capturedDelays?.Add(delay);
                onDelay?.Invoke(delay);
                return Task.Delay(1, ct);
            },
            pollTimeout: TimeSpan.FromMilliseconds(20),
            watermarkTimeout: TimeSpan.FromMilliseconds(20),
            metadataSource: metadataSource,
            requireSinglePartition: requireSinglePartition);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        Assert.True(condition(), "Timeout esperando condición");
    }

    private static async Task AwaitCancelledAsync(Task run)
    {
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            Assert.Fail("Pump no terminó tras cancelación");
        }
    }

    private sealed class FailingCreateFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly int _failCount;
        private readonly Action _onHealthyIdle;

        public FailingCreateFactory(int failCount, Action onHealthyIdle)
        {
            _failCount = failCount;
            _onHealthyIdle = onHealthyIdle;
        }

        public int CreateCount { get; private set; }

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            if (CreateCount <= _failCount)
                throw new IOException($"transient-create-{CreateCount}");

            return new PumpSession(failFatallyOnce: false, onHealthyIdle: _onHealthyIdle);
        }
    }

    private sealed class AlwaysFailCreateFactory : IRealtimeKafkaConsumerFactory
    {
        public int CreateCount { get; private set; }

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            throw new IOException("persistent-create-failure");
        }
    }

    private sealed class FailWatermarksThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _onHealthyIdle;

        public FailWatermarksThenIdleFactory(Action onHealthyIdle) => _onHealthyIdle = onHealthyIdle;

        public int CreateCount { get; private set; }
        public List<PumpSession> Sessions { get; } = [];

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            var session = new PumpSession(
                failFatallyOnce: false,
                onHealthyIdle: CreateCount == 1 ? null : _onHealthyIdle,
                failWatermarksOnce: CreateCount == 1);
            Sessions.Add(session);
            return session;
        }
    }

    private sealed class FailAssignThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _onHealthyIdle;

        public FailAssignThenIdleFactory(Action onHealthyIdle) => _onHealthyIdle = onHealthyIdle;

        public int CreateCount { get; private set; }
        public int AssignSuccessCount { get; private set; }
        public List<PumpSession> Sessions { get; } = [];

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            var session = new PumpSession(
                failFatallyOnce: false,
                onHealthyIdle: CreateCount == 1 ? null : _onHealthyIdle,
                failAssignOnce: CreateCount == 1,
                onAssignSuccess: () => AssignSuccessCount++);
            Sessions.Add(session);
            return session;
        }
    }

    private sealed class FailMaterializeThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _onHealthyIdle;

        public FailMaterializeThenIdleFactory(Action onHealthyIdle) => _onHealthyIdle = onHealthyIdle;

        public int CreateCount { get; private set; }
        public List<PumpSession> Sessions { get; } = [];

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            var session = new PumpSession(
                failFatallyOnce: false,
                onHealthyIdle: CreateCount == 1 ? null : _onHealthyIdle,
                failMaterializeOnce: CreateCount == 1);
            Sessions.Add(session);
            return session;
        }
    }

    private sealed class FatalThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _stopAfterHealthyIdle;

        public FatalThenIdleFactory(Action stopAfterHealthyIdle) =>
            _stopAfterHealthyIdle = stopAfterHealthyIdle;

        public int CreateCount { get; private set; }
        public List<PumpSession> Sessions { get; } = [];

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            var session = new PumpSession(
                failFatallyOnce: CreateCount == 1,
                onHealthyIdle: CreateCount == 1 ? null : _stopAfterHealthyIdle);
            Sessions.Add(session);
            return session;
        }
    }

    private sealed class AlwaysFatalFactory : IRealtimeKafkaConsumerFactory
    {
        public int CreateCount { get; private set; }

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            return new PumpSession(failFatallyOnce: true, onHealthyIdle: null, alwaysFatal: true);
        }
    }

    private sealed class FailCreateThenFatalFactory : IRealtimeKafkaConsumerFactory
    {
        public int CreateCount { get; private set; }

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            if (CreateCount == 1)
                throw new IOException("transient-create");

            // Assign OK, Fatal inmediato sin Idle/Completed.
            return new PumpSession(failFatallyOnce: true, onHealthyIdle: null, alwaysFatal: true);
        }
    }

    private sealed class FailTwiceThenIdleThenFatalThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _stopAfterFinalIdle;

        public FailTwiceThenIdleThenFatalThenIdleFactory(Action stopAfterFinalIdle) =>
            _stopAfterFinalIdle = stopAfterFinalIdle;

        public int CreateCount { get; private set; }

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            if (CreateCount <= 2)
                throw new IOException($"transient-create-{CreateCount}");

            if (CreateCount == 3)
            {
                // Idle saludable (reinicia racha) y luego Fatal en el mismo ciclo de sesión.
                return new PumpSession(
                    failFatallyOnce: true,
                    onHealthyIdle: null,
                    idleThenFatal: true);
            }

            return new PumpSession(failFatallyOnce: false, onHealthyIdle: _stopAfterFinalIdle);
        }
    }

    private sealed class FailTwiceThenCompletedThenFatalThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _stopAfterFinalIdle;

        public FailTwiceThenCompletedThenFatalThenIdleFactory(Action stopAfterFinalIdle) =>
            _stopAfterFinalIdle = stopAfterFinalIdle;

        public int CreateCount { get; private set; }

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            if (CreateCount <= 2)
                throw new IOException($"transient-create-{CreateCount}");

            if (CreateCount == 3)
            {
                return new PumpSession(
                    failFatallyOnce: true,
                    onHealthyIdle: null,
                    completedThenFatal: true);
            }

            return new PumpSession(failFatallyOnce: false, onHealthyIdle: _stopAfterFinalIdle);
        }
    }

    private sealed class IdleThenFatalThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _stopAfterFinalIdle;

        public IdleThenFatalThenIdleFactory(Action stopAfterFinalIdle) =>
            _stopAfterFinalIdle = stopAfterFinalIdle;

        public int CreateCount { get; private set; }

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            if (CreateCount == 1)
            {
                return new PumpSession(
                    failFatallyOnce: true,
                    onHealthyIdle: null,
                    idleThenFatal: true);
            }

            return new PumpSession(failFatallyOnce: false, onHealthyIdle: _stopAfterFinalIdle);
        }
    }

    private sealed class AssignThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _stopAfterHealthyIdle;
        private readonly TaskCompletionSource _releaseIdle =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AssignThenIdleFactory(Action stopAfterHealthyIdle) =>
            _stopAfterHealthyIdle = stopAfterHealthyIdle;

        public bool Assigned { get; private set; }
        public int CreateCount { get; private set; }

        public void ReleaseIdle() => _releaseIdle.TrySetResult();

        public IRealtimeKafkaConsumerSession Create(string groupId)
        {
            CreateCount++;
            return new PumpSession(
                failFatallyOnce: false,
                onHealthyIdle: _stopAfterHealthyIdle,
                onAssign: () => Assigned = true,
                waitBeforeIdle: _releaseIdle.Task);
        }
    }

    private sealed class SequenceMetadataSource : IRealtimeTopicMetadataSource
    {
        private readonly Func<int>[] _steps;

        public SequenceMetadataSource(params Func<int>[] steps) => _steps = steps;

        public int CallCount { get; private set; }

        public int GetPartitionCount(string topic, TimeSpan timeout)
        {
            var index = Math.Min(CallCount, _steps.Length - 1);
            CallCount++;
            return _steps[index]();
        }
    }

    private sealed class PumpSession : IRealtimeKafkaConsumerSession
    {
        private readonly bool _failFatallyOnce;
        private readonly Action? _onHealthyIdle;
        private readonly Action? _onAssign;
        private readonly Action? _onAssignSuccess;
        private readonly Task? _waitBeforeIdle;
        private readonly bool _failWatermarksOnce;
        private readonly bool _failAssignOnce;
        private readonly bool _failMaterializeOnce;
        private readonly bool _alwaysFatal;
        private readonly bool _idleThenFatal;
        private readonly bool _completedThenFatal;
        private bool _fatalConsumed;
        private bool _idleEmitted;
        private bool _completedEmitted;
        private bool _watermarksFailed;
        private bool _assignFailed;
        private bool _materializeFailed;
        private readonly List<TopicPartition> _assignment = [];

        public PumpSession(
            bool failFatallyOnce,
            Action? onHealthyIdle,
            Action? onAssign = null,
            Task? waitBeforeIdle = null,
            bool failWatermarksOnce = false,
            bool failAssignOnce = false,
            bool failMaterializeOnce = false,
            Action? onAssignSuccess = null,
            bool alwaysFatal = false,
            bool idleThenFatal = false,
            bool completedThenFatal = false)
        {
            _failFatallyOnce = failFatallyOnce;
            _onHealthyIdle = onHealthyIdle;
            _onAssign = onAssign;
            _waitBeforeIdle = waitBeforeIdle;
            _failWatermarksOnce = failWatermarksOnce;
            _failAssignOnce = failAssignOnce;
            _failMaterializeOnce = failMaterializeOnce;
            _onAssignSuccess = onAssignSuccess;
            _alwaysFatal = alwaysFatal;
            _idleThenFatal = idleThenFatal;
            _completedThenFatal = completedThenFatal;
        }

        public bool Disposed { get; private set; }
        public ConsumeResult<string, string>? PrefetchProbe { get; private set; }
        public IReadOnlyList<TopicPartition> Assignment => _assignment;

        public WatermarkOffsets QueryWatermarkOffsets(TopicPartition partition, TimeSpan timeout)
        {
            if (_failWatermarksOnce && !_watermarksFailed)
            {
                _watermarksFailed = true;
                throw new KafkaException(new Error(ErrorCode.Local_Transport, "watermark-timeout", isFatal: false));
            }

            return new WatermarkOffsets(new Offset(0), new Offset(1));
        }

        public void Assign(IEnumerable<TopicPartitionOffset> offsets)
        {
            if (_failAssignOnce && !_assignFailed)
            {
                _assignFailed = true;
                throw new KafkaException(new Error(ErrorCode.Local_Transport, "assign-transient", isFatal: false));
            }

            if (_failMaterializeOnce && !_materializeFailed)
            {
                _materializeFailed = true;
                PrefetchProbe = null;
                throw new RealtimeKafkaAssignmentMaterializationException(
                    topic: "fleet.realtime",
                    partition: 0,
                    targetOffset: 1);
            }

            _assignment.Clear();
            foreach (var tpo in offsets)
                _assignment.Add(tpo.TopicPartition);
            _onAssign?.Invoke();
            _onAssignSuccess?.Invoke();
        }

        public ConsumeResult<string, string>? Consume(TimeSpan timeout)
        {
            if (_alwaysFatal || (_failFatallyOnce && !_fatalConsumed && !_idleThenFatal && !_completedThenFatal))
            {
                _fatalConsumed = true;
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]> { Topic = "fleet.realtime", Partition = 0, Offset = 0 },
                    new Error(ErrorCode.Local_Fatal, "simulated-fatal", isFatal: true));
            }

            if (_idleThenFatal)
            {
                if (!_idleEmitted)
                {
                    _idleEmitted = true;
                    return null;
                }

                _fatalConsumed = true;
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]> { Topic = "fleet.realtime", Partition = 0, Offset = 0 },
                    new Error(ErrorCode.Local_Fatal, "simulated-fatal-after-idle", isFatal: true));
            }

            if (_completedThenFatal)
            {
                if (!_completedEmitted)
                {
                    _completedEmitted = true;
                    // Offset 0 con baseline High-1=0 → Duplicate → Completed (poll saludable).
                    return ValidVehicleConsume(offset: 0);
                }

                _fatalConsumed = true;
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]> { Topic = "fleet.realtime", Partition = 0, Offset = 0 },
                    new Error(ErrorCode.Local_Fatal, "simulated-fatal-after-completed", isFatal: true));
            }

            if (_waitBeforeIdle is { IsCompleted: false })
            {
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]> { Topic = "fleet.realtime", Partition = 0, Offset = 0 },
                    new Error(ErrorCode.Local_Transport, "waiting-release", isFatal: false));
            }

            if (!_idleEmitted)
            {
                _idleEmitted = true;
                _onHealthyIdle?.Invoke();
            }

            return null;
        }

        public void Close()
        {
        }

        public void Dispose() => Disposed = true;

        private static ConsumeResult<string, string> ValidVehicleConsume(long offset) =>
            new()
            {
                Topic = "fleet.realtime",
                Partition = 0,
                Offset = offset,
                Message = new Message<string, string>
                {
                    Value = FleetRealtimeKafkaMessage.Serialize(new FleetRealtimeKafkaMessage
                    {
                        SchemaVersion = FleetRealtimeKafkaMessage.CurrentSchemaVersion,
                        EventType = FleetRealtimeEventTypes.VehicleUpdate,
                        Payload = System.Text.Json.JsonDocument.Parse(
                            """{"deviceId":"11111111-1111-1111-1111-111111111111","vehicleName":"VH-001","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"}""").RootElement,
                        OccurredAt = DateTimeOffset.UtcNow,
                        DeviceId = "11111111-1111-1111-1111-111111111111"
                    })
                }
            };
    }
}
