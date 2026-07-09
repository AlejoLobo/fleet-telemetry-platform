namespace FleetTelemetry.Application.Exceptions;

public class DependencyCircuitOpenException : Exception
{
    public DependencyCircuitOpenException(string dependency, TimeSpan? retryAfter = null)
        : base($"El circuit breaker de {dependency} está abierto; la dependencia no está disponible temporalmente.")
    {
        Dependency = dependency;
        RetryAfter = retryAfter;
    }

    public string Dependency { get; }

    public TimeSpan? RetryAfter { get; }
}
