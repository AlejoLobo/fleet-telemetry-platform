// Opciones de políticas de resiliencia Polly.
namespace FleetTelemetry.Infrastructure.Configuration;

// Umbrales de circuit breaker por dependencia.
public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public CircuitBreakerPolicyOptions Kafka { get; set; } = new();

    public CircuitBreakerPolicyOptions OpenAi { get; set; } = new();

    public CircuitBreakerPolicyOptions TimescaleDb { get; set; } = new();
}

// Parámetros de una política de circuit breaker.
public class CircuitBreakerPolicyOptions
{
    public bool Enabled { get; set; } = true;

    public double FailureRatio { get; set; } = 0.5;

    public int MinimumThroughput { get; set; } = 5;

    public int SamplingDurationSeconds { get; set; } = 30;

    public int BreakDurationSeconds { get; set; } = 30;

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 200;
}
