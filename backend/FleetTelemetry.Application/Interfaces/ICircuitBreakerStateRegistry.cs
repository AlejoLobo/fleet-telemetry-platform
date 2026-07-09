// Contrato de registro de estado de circuit breakers.
namespace FleetTelemetry.Application.Interfaces;

// Dependencias externas con resiliencia.
public enum ResilienceDependency
{
    Kafka,
    OpenAi,
    TimescaleDb
}

// Estados posibles del circuit breaker.
public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

// Instantánea del estado de una dependencia.
public record CircuitBreakerSnapshot(
    string Dependency,
    string State,
    DateTimeOffset? LastStateChangeUtc,
    int FailureCountInWindow);

// Registro en memoria de transiciones y fallos.
public interface ICircuitBreakerStateRegistry
{
    // Registra transición de estado.
    void RecordTransition(ResilienceDependency dependency, CircuitBreakerState state);

    // Incrementa contador de fallos en ventana.
    void RecordFailure(ResilienceDependency dependency);

    // Devuelve estado actual de todas las dependencias.
    IReadOnlyList<CircuitBreakerSnapshot> GetSnapshots();

    // Indica si el circuito está abierto.
    bool IsOpen(ResilienceDependency dependency);
}
