namespace FleetTelemetry.Application.Interfaces;

public enum ResilienceDependency
{
    Kafka,
    OpenAi,
    TimescaleDb
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

public record CircuitBreakerSnapshot(
    string Dependency,
    string State,
    DateTimeOffset? LastStateChangeUtc,
    int FailureCountInWindow);

public interface ICircuitBreakerStateRegistry
{
    void RecordTransition(ResilienceDependency dependency, CircuitBreakerState state);

    // Incrementa contador de fallos en ventana.
    void RecordFailure(ResilienceDependency dependency);

    IReadOnlyList<CircuitBreakerSnapshot> GetSnapshots();

    bool IsOpen(ResilienceDependency dependency);
}
