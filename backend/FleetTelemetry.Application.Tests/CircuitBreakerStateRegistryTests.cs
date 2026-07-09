using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Resilience;

// Pruebas del registro de circuit breakers.
namespace FleetTelemetry.Application.Tests;

public class CircuitBreakerStateRegistryTests
{
    [Fact]
    public void IsOpen_returns_false_initially()
    {
        var registry = new CircuitBreakerStateRegistry();
        Assert.False(registry.IsOpen(ResilienceDependency.Kafka));
    }

    [Fact]
    public void RecordTransition_marks_dependency_open()
    {
        var registry = new CircuitBreakerStateRegistry();
        registry.RecordTransition(ResilienceDependency.Kafka, CircuitBreakerState.Open);

        Assert.True(registry.IsOpen(ResilienceDependency.Kafka));
        Assert.Contains(registry.GetSnapshots(), s => s.Dependency == "Kafka" && s.State == "Open");
    }

    [Fact]
    public void RecordTransition_closed_resets_open_state()
    {
        var registry = new CircuitBreakerStateRegistry();
        registry.RecordTransition(ResilienceDependency.TimescaleDb, CircuitBreakerState.Open);
        registry.RecordTransition(ResilienceDependency.TimescaleDb, CircuitBreakerState.Closed);

        Assert.False(registry.IsOpen(ResilienceDependency.TimescaleDb));
    }
}
