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

        // Assign solo prepara; Transient no habilita Ready desde Starting.
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
                        """{"vehicleId":"VH-001","name":"VH-001","status":"online","lastSeenAt":"2026-07-13T10:00:00Z"}""").RootElement,
                    OccurredAt = DateTimeOffset.UtcNow,
                    VehicleId = "VH-001"
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

// Pump productivo: recreación de consumidor ante FatalFailure (sin bucles Transient).
public class KafkaManualAssignmentPumpTests
{
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

    private static KafkaManualAssignmentPump CreatePump(
        FleetSseBroker broker,
        IRealtimeStreamCoordinator coordinator,
        IRealtimeKafkaConsumerFactory factory) =>
        new(
            broker,
            coordinator,
            factory,
            topic: "fleet.realtime",
            groupId: "fleet-realtime-sse-test",
            delayAsync: static (_, ct) => Task.Delay(1, ct),
            pollTimeout: TimeSpan.FromMilliseconds(20),
            watermarkTimeout: TimeSpan.FromMilliseconds(20));

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

    private sealed class AssignThenIdleFactory : IRealtimeKafkaConsumerFactory
    {
        private readonly Action _stopAfterHealthyIdle;
        private readonly TaskCompletionSource _releaseIdle =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AssignThenIdleFactory(Action stopAfterHealthyIdle) =>
            _stopAfterHealthyIdle = stopAfterHealthyIdle;

        public bool Assigned { get; private set; }

        public void ReleaseIdle() => _releaseIdle.TrySetResult();

        public IRealtimeKafkaConsumerSession Create(string groupId) =>
            new PumpSession(
                failFatallyOnce: false,
                onHealthyIdle: _stopAfterHealthyIdle,
                onAssign: () => Assigned = true,
                waitBeforeIdle: _releaseIdle.Task);
    }

    private sealed class PumpSession : IRealtimeKafkaConsumerSession
    {
        private readonly bool _failFatallyOnce;
        private readonly Action? _onHealthyIdle;
        private readonly Action? _onAssign;
        private readonly Task? _waitBeforeIdle;
        private bool _fatalConsumed;
        private bool _idleEmitted;
        private readonly List<TopicPartition> _assignment = [];

        public PumpSession(
            bool failFatallyOnce,
            Action? onHealthyIdle,
            Action? onAssign = null,
            Task? waitBeforeIdle = null)
        {
            _failFatallyOnce = failFatallyOnce;
            _onHealthyIdle = onHealthyIdle;
            _onAssign = onAssign;
            _waitBeforeIdle = waitBeforeIdle;
        }

        public bool Disposed { get; private set; }
        public IReadOnlyList<TopicPartition> Assignment => _assignment;

        public WatermarkOffsets QueryWatermarkOffsets(TopicPartition partition, TimeSpan timeout) =>
            new(new Offset(0), new Offset(1));

        public void Assign(IEnumerable<TopicPartitionOffset> offsets)
        {
            _assignment.Clear();
            foreach (var tpo in offsets)
                _assignment.Add(tpo.TopicPartition);
            _onAssign?.Invoke();
        }

        public ConsumeResult<string, string>? Consume(TimeSpan timeout)
        {
            if (_failFatallyOnce && !_fatalConsumed)
            {
                _fatalConsumed = true;
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]> { Topic = "fleet.realtime", Partition = 0, Offset = 0 },
                    new Error(ErrorCode.Local_Fatal, "simulated-fatal", isFatal: true));
            }

            // Hasta ReleaseIdle: Transient (Starting permanece); null sería Idle→Ready prematuro.
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
    }
}

