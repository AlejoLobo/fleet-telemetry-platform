using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Realtime;

namespace FleetTelemetry.Application.Tests;

// FT-005: admisión SSE atómica y estados Starting/Ready/Recovering/Faulted.
public class RealtimeStreamCoordinatorTests
{
    [Fact]
    public void No_existen_suscriptores_fuera_de_Ready()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);

        Assert.Equal(RealtimeStreamState.Starting, coordinator.State);
        var denied = coordinator.TryOpenStream(new SseLastEventId.Missing());
        Assert.False(denied.Admitted);
        Assert.Equal(0, broker.SubscriberCount);

        coordinator.EnterRecovering("backoff");
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
        Assert.Equal(0, broker.SubscriberCount);

        coordinator.EnterFaulted("boom");
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public void TryOpenStream_y_EnterFaulted_son_atomicos()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(baselineOffset: 10);

        var opened = coordinator.TryOpenStream(new SseLastEventId.Missing());
        Assert.True(opened.Admitted);
        Assert.Equal(1, broker.SubscriberCount);

        coordinator.EnterFaulted("fatal");
        Assert.Equal(RealtimeStreamState.Faulted, coordinator.State);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.True(opened.Subscription!.LiveReader.Completion.IsCompleted);

        var rejected = coordinator.TryOpenStream(new SseLastEventId.Missing());
        Assert.False(rejected.Admitted);
        Assert.Equal(RealtimeStreamState.Faulted, rejected.State);
        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public async Task Cien_intentos_concurrentes_durante_EnterFaulted_dejan_SubscriberCount_cero()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(0);

        var barrier = new Barrier(101);
        var openTasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return coordinator.TryOpenStream(new SseLastEventId.Missing());
        })).ToArray();

        var faultTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            coordinator.EnterFaulted("concurrent-fault");
        });

        await Task.WhenAll(openTasks.Cast<Task>().Append(faultTask));

        Assert.Equal(RealtimeStreamState.Faulted, coordinator.State);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.All(openTasks, t =>
        {
            if (t.Result.Admitted)
                Assert.True(t.Result.Subscription!.LiveReader.Completion.IsCompleted);
        });
    }

    [Fact]
    public void EnterRecovering_cierra_suscriptores_e_impide_admision()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(5);
        var epochBefore = coordinator.Epoch;

        Assert.True(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
        coordinator.EnterRecovering("transient");
        Assert.Equal(RealtimeStreamState.Recovering, coordinator.State);
        Assert.Equal(epochBefore + 1, coordinator.Epoch);
        Assert.Equal(0, broker.SubscriberCount);
        Assert.False(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);

        coordinator.EnterReady();
        Assert.True(coordinator.IsReady);
        Assert.True(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
    }

    [Fact]
    public void MarkBypassed_admite_SSE_sin_KafkaPush()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.MarkBypassed();
        Assert.True(coordinator.IsReady);
        Assert.True(coordinator.TryOpenStream(new SseLastEventId.Missing()).Admitted);
    }

    [Fact]
    public void EnterFaulted_no_vuelve_a_Ready()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(0);
        coordinator.EnterFaulted("fatal");
        coordinator.EnterReady(1);
        Assert.Equal(RealtimeStreamState.Faulted, coordinator.State);
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public void Baseline_se_expone_tras_EnterReady()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var coordinator = new RealtimeStreamCoordinator(broker);
        coordinator.EnterReady(99);
        Assert.Equal(99, coordinator.BaselineOffset);
    }
}
