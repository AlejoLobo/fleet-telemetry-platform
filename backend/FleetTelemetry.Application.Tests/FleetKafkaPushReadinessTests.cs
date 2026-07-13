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
    public void Consume_fatal_marca_readiness_Faulted()
    {
        var readiness = new FleetKafkaPushReadiness();
        readiness.EstablishFirstAssignmentPosition(0);
        readiness.MarkReady();

        readiness.MarkFaulted("Fatal Kafka consume error: Local: Fatal error");
        Assert.Equal(FleetKafkaPushReadinessState.Faulted, readiness.State);
        Assert.False(readiness.IsReady);
        Assert.Contains("Fatal", readiness.FaultReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Notifica_ciclo_exitoso_marca_Ready_tras_asignacion()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        var readiness = new FleetKafkaPushReadiness();
        var coordinator = new FleetKafkaPushAssignmentCoordinator(broker, readiness);

        readiness.MarkAssigned();
        coordinator.ResolveResumeOffset(10, -1, null, false);
        Assert.False(readiness.IsReady);

        // Simula await Ready pendiente tras HandlePartitionsAssigned.
        typeof(FleetKafkaPushAssignmentCoordinator)
            .GetField("_awaitingReadyAfterAssignment",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(coordinator, true);

        coordinator.NotifySuccessfulPollCycle();
        Assert.True(readiness.IsReady);
    }
}
