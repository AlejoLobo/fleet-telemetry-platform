using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetTelemetry.Application.Tests;

public class FleetKafkaPushReadinessTests
{
    [Fact]
    public void MarkReady_sin_posicion_inicial_falla()
    {
        var readiness = new FleetKafkaPushReadiness();
        readiness.MarkAssigned();
        Assert.Throws<InvalidOperationException>(() => readiness.MarkReady());
        Assert.Equal(FleetKafkaPushReadinessState.Assigned, readiness.State);
        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void Transicion_Assigned_Position_Ready()
    {
        var readiness = new FleetKafkaPushReadiness();
        Assert.Equal(FleetKafkaPushReadinessState.Starting, readiness.State);

        readiness.MarkAssigned();
        Assert.Equal(FleetKafkaPushReadinessState.Assigned, readiness.State);

        readiness.EstablishFirstAssignmentPosition(42);
        Assert.Equal(42, readiness.InitialPositionOffset);
        Assert.Equal(42, readiness.CurrentResumeOffset);

        readiness.MarkReady();
        Assert.True(readiness.IsReady);
        Assert.True(readiness.HasCompletedFirstAssignment);
        Assert.Equal(FleetKafkaPushReadinessState.Ready, readiness.State);
    }

    [Fact]
    public void Ready_pasa_a_no_ready_al_revocar_particion()
    {
        var readiness = new FleetKafkaPushReadiness();
        readiness.EstablishFirstAssignmentPosition(100);
        readiness.MarkReady();
        Assert.True(readiness.IsReady);

        readiness.MarkRebalancing();
        Assert.False(readiness.IsReady);
        Assert.Equal(FleetKafkaPushReadinessState.Rebalancing, readiness.State);
        Assert.Equal(100, readiness.InitialPositionOffset);
    }

    [Fact]
    public void MarkFaulted_bloquea_Ready()
    {
        var readiness = new FleetKafkaPushReadiness();
        readiness.MarkAssigned();
        readiness.EstablishFirstAssignmentPosition(0);
        readiness.MarkFaulted("boom");

        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
        readiness.MarkReady();
        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
        Assert.False(readiness.IsReady);
        Assert.Equal("boom", readiness.FaultReason);
    }

    [Fact]
    public void MarkBypassed_deja_Ready_para_Polling()
    {
        var readiness = new FleetKafkaPushReadiness();
        readiness.MarkBypassed();
        Assert.True(readiness.IsReady);
    }
}

public class FleetKafkaPushAssignmentCoordinatorTests
{
    [Fact]
    public void Reasignacion_no_vuelve_al_high_watermark()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(
            broker, readiness, null);

        var first = coordinator.ResolveResumeOffset(
            kafkaHighWatermark: 100,
            lastProcessedExternalOffset: -1,
            initialPositionOffset: null,
            hasCompletedFirstAssignment: false);
        Assert.Equal(100, first);
        readiness.MarkReady();

        for (var offset = 100L; offset <= 105; offset++)
            Assert.Equal(Application.Realtime.ExternalPublishResult.Accepted,
                broker.PublishExternal(offset, "alert", new { n = offset }));

        Assert.Equal(105, broker.LastProcessedExternalOffset);

        readiness.MarkRebalancing();
        readiness.MarkAssigned();

        var resume = coordinator.ResolveResumeOffset(
            kafkaHighWatermark: 110,
            lastProcessedExternalOffset: broker.LastProcessedExternalOffset,
            initialPositionOffset: readiness.InitialPositionOffset,
            hasCompletedFirstAssignment: true);

        Assert.Equal(106, resume);
        Assert.Equal(100, readiness.InitialPositionOffset);
        Assert.Equal(106, readiness.CurrentResumeOffset);
    }

    [Fact]
    public void Reasignacion_continua_desde_LastProcessedOffset_mas_uno()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);

        coordinator.ResolveResumeOffset(50, -1, null, false);
        readiness.MarkReady();
        broker.PublishExternal(50, "alert", new { n = 50 });
        broker.PublishExternal(51, "alert", new { n = 51 });

        readiness.MarkRebalancing();
        readiness.MarkAssigned();
        var resume = coordinator.ResolveResumeOffset(
            kafkaHighWatermark: 999,
            lastProcessedExternalOffset: broker.LastProcessedExternalOffset,
            initialPositionOffset: readiness.InitialPositionOffset,
            hasCompletedFirstAssignment: true);

        Assert.Equal(52, resume);
    }

    [Fact]
    public void Sin_eventos_procesados_reanuda_desde_InitialPositionOffset()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);

        coordinator.ResolveResumeOffset(100, -1, null, false);
        readiness.MarkReady();
        Assert.Equal(-1, broker.LastProcessedExternalOffset);

        readiness.MarkRebalancing();
        readiness.MarkAssigned();
        var resume = coordinator.ResolveResumeOffset(
            kafkaHighWatermark: 150,
            lastProcessedExternalOffset: broker.LastProcessedExternalOffset,
            initialPositionOffset: readiness.InitialPositionOffset,
            hasCompletedFirstAssignment: true);

        Assert.Equal(100, resume);
    }

    [Fact]
    public void Consume_fatal_real_marca_Faulted()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        var transport = new ThrowingKafkaPushTransport(
            () => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_Fatal, "fatal consume")));
        var loop = new FleetRealtimeKafkaPushLoop(
            new RealtimeKafkaPushProcessor(broker),
            delayAsync: static (_, _) => Task.CompletedTask,
            backoff: TimeSpan.Zero);

        ArmAwaitingReady(coordinator, readiness);

        var pollResult = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.FatalFailure, pollResult);

        // Misma orquestación que FleetSseKafkaPushHostedService ante FatalFailure.
        coordinator.EnterFaulted("Fatal Kafka consume error");

        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
        Assert.False(readiness.IsReady);
        Assert.Contains("Fatal", readiness.FaultReason, StringComparison.Ordinal);
        Assert.False(coordinator.AwaitingReadyAfterAssignment);
    }

    [Fact]
    public void Consume_fatal_no_ejecuta_NotifySuccessfulPollCycle()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        var transport = new ThrowingKafkaPushTransport(
            () => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_Fatal, "fatal consume")));
        var loop = new FleetRealtimeKafkaPushLoop(
            new RealtimeKafkaPushProcessor(broker),
            delayAsync: static (_, _) => Task.CompletedTask,
            backoff: TimeSpan.Zero);

        ArmAwaitingReady(coordinator, readiness);
        Assert.True(coordinator.AwaitingReadyAfterAssignment);

        var pollResult = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.FatalFailure, pollResult);

        // Fatal no notifica éxito: Ready no se habilita por este camino.
        if (pollResult == KafkaPushPollResult.Successful)
            coordinator.NotifySuccessfulPollCycle();

        Assert.False(readiness.IsReady);
        Assert.Equal(FleetKafkaPushReadinessState.Assigned, readiness.State);
        Assert.True(coordinator.AwaitingReadyAfterAssignment);

        coordinator.EnterFaulted("Fatal Kafka consume error");
        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void Consume_no_fatal_no_marca_Ready_hasta_un_poll_exitoso()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        var transport = new ThrowingKafkaPushTransport(
            () => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_Transport, "transient consume")));
        var loop = new FleetRealtimeKafkaPushLoop(
            new RealtimeKafkaPushProcessor(broker),
            delayAsync: static (_, _) => Task.CompletedTask,
            backoff: TimeSpan.Zero);

        ArmAwaitingReady(coordinator, readiness);

        var transient = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.TransientFailure, transient);
        if (transient == KafkaPushPollResult.Successful)
            coordinator.NotifySuccessfulPollCycle();

        Assert.False(readiness.IsReady);
        Assert.Equal(FleetKafkaPushReadinessState.Assigned, readiness.State);
        Assert.True(coordinator.AwaitingReadyAfterAssignment);

        transport.ThrowOnConsume = null;
        var success = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.Successful, success);
        coordinator.NotifySuccessfulPollCycle();
        Assert.True(readiness.IsReady);
    }

    [Fact]
    public void Primer_poll_fallido_despues_de_asignacion_no_habilita_SSE()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        var transport = new ThrowingKafkaPushTransport(
            () => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_Fail, "first poll failed")));
        var loop = new FleetRealtimeKafkaPushLoop(
            new RealtimeKafkaPushProcessor(broker),
            delayAsync: static (_, _) => Task.CompletedTask,
            backoff: TimeSpan.Zero);

        ArmAwaitingReady(coordinator, readiness);
        Assert.False(readiness.IsReady);

        var result = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.TransientFailure, result);
        if (result == KafkaPushPollResult.Successful)
            coordinator.NotifySuccessfulPollCycle();

        Assert.False(readiness.IsReady);
        Assert.Equal(FleetKafkaPushReadinessState.Assigned, readiness.State);
    }

    [Fact]
    public void Poll_exitoso_posterior_marca_Ready()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        var transport = new ThrowingKafkaPushTransport(
            () => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_Fail, "first poll failed")));
        var loop = new FleetRealtimeKafkaPushLoop(
            new RealtimeKafkaPushProcessor(broker),
            delayAsync: static (_, _) => Task.CompletedTask,
            backoff: TimeSpan.Zero);

        ArmAwaitingReady(coordinator, readiness);

        var failed = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.TransientFailure, failed);
        if (failed == KafkaPushPollResult.Successful)
            coordinator.NotifySuccessfulPollCycle();
        Assert.False(readiness.IsReady);

        transport.ThrowOnConsume = null;
        var ok = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.Successful, ok);
        coordinator.NotifySuccessfulPollCycle();
        Assert.True(readiness.IsReady);
        Assert.Equal(FleetKafkaPushReadinessState.Ready, readiness.State);
    }

    [Fact]
    public void Conexion_existente_se_cierra_al_perder_particion()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        readiness.EstablishFirstAssignmentPosition(0);
        readiness.MarkReady();

        var subscription = broker.SubscribeFrom(new Application.Realtime.SseLastEventId.Missing());
        Assert.Equal(1, broker.SubscriberCount);

        coordinator.HandlePartitionsLost([]);

        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.True(subscription.LiveReader.Completion.IsCompleted);
    }

    [Fact]
    public void Conexion_existente_se_cierra_ante_consume_fatal()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        var transport = new ThrowingKafkaPushTransport(
            () => throw new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_Fatal, "fatal consume")));
        var loop = new FleetRealtimeKafkaPushLoop(
            new RealtimeKafkaPushProcessor(broker),
            delayAsync: static (_, _) => Task.CompletedTask,
            backoff: TimeSpan.Zero);

        ArmAwaitingReady(coordinator, readiness);
        var subscription = broker.SubscribeFrom(new Application.Realtime.SseLastEventId.Missing());
        Assert.Equal(1, broker.SubscriberCount);

        var pollResult = loop.RunOnce(transport, TimeSpan.FromMilliseconds(50));
        Assert.Equal(KafkaPushPollResult.FatalFailure, pollResult);
        coordinator.EnterFaulted("Fatal Kafka consume error");

        Assert.Equal(0, broker.SubscriberCount);
        Assert.True(subscription.LiveReader.Completion.IsCompleted);
        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
    }

    [Fact]
    public void Heartbeat_no_mantiene_stream_vivo_en_Faulted()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        readiness.EstablishFirstAssignmentPosition(0);
        readiness.MarkReady();

        var subscription = broker.SubscribeFrom(new Application.Realtime.SseLastEventId.Missing());
        // Descarta stream-reset inicial.
        while (subscription.LiveReader.TryRead(out _)) { }

        coordinator.HandlePartitionsLost([]);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.True(subscription.LiveReader.Completion.IsCompleted);

        broker.PublishEphemeral(
            Application.Realtime.FleetRealtimeEventTypes.Heartbeat,
            new { status = "ok" });

        Assert.False(subscription.LiveReader.TryRead(out _));
        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public void SubscriberCount_queda_en_cero()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);
        readiness.EstablishFirstAssignmentPosition(0);
        readiness.MarkReady();

        broker.SubscribeFrom(new Application.Realtime.SseLastEventId.Missing());
        broker.SubscribeFrom(new Application.Realtime.SseLastEventId.Missing());
        Assert.Equal(2, broker.SubscriberCount);

        coordinator.EnterFaulted("irreversible startup failure");
        Assert.Equal(0, broker.SubscriberCount);
        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
    }

    private static void ArmAwaitingReady(
        FleetKafkaPushAssignmentCoordinator coordinator,
        FleetKafkaPushReadiness readiness)
    {
        readiness.MarkAssigned();
        coordinator.ResolveResumeOffset(10, -1, null, false);
        Assert.False(readiness.IsReady);
        typeof(FleetKafkaPushAssignmentCoordinator)
            .GetField("_awaitingReadyAfterAssignment",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(coordinator, true);
    }

    private sealed class ThrowingKafkaPushTransport : IRealtimeKafkaPushTransport
    {
        public Func<ConsumeResult<string, string>?>? ThrowOnConsume { get; set; }

        public ThrowingKafkaPushTransport(Func<ConsumeResult<string, string>?> throwOnConsume) =>
            ThrowOnConsume = throwOnConsume;

        public ConsumeResult<string, string>? Consume(TimeSpan timeout)
        {
            _ = timeout;
            if (ThrowOnConsume is not null)
                return ThrowOnConsume();

            return null;
        }

        public void Commit(ConsumeResult<string, string> result) => _ = result;

        public void Seek(long offset) => _ = offset;
    }
}
