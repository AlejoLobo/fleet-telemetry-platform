namespace FleetTelemetry.Infrastructure.Configuration;

public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public CircuitBreakerPolicyOptions Kafka { get; set; } = new();

    public CircuitBreakerPolicyOptions OpenAi { get; set; } = new();

    public CircuitBreakerPolicyOptions TimescaleDb { get; set; } = new();
}

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
