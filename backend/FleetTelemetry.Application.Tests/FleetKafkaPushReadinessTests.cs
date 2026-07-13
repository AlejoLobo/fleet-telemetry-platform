using FleetTelemetry.Infrastructure.Realtime;

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

        readiness.EstablishInitialPosition(42);
        Assert.Equal(42, readiness.InitialPositionOffset);

        readiness.MarkReady();
        Assert.True(readiness.IsReady);
        Assert.Equal(FleetKafkaPushReadinessState.Ready, readiness.State);
    }

    [Fact]
    public void MarkFaulted_bloquea_Ready()
    {
        var readiness = new FleetKafkaPushReadiness();
        readiness.MarkAssigned();
        readiness.EstablishInitialPosition(0);
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
