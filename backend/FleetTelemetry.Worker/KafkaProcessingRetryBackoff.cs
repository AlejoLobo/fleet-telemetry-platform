namespace FleetTelemetry.Worker;

// Cálculo de backoff exponencial con jitter acotado al 25 % del delay base.
public static class KafkaProcessingRetryBackoff
{
    private const int MaxExponent = 20;
    private const double JitterFraction = 0.25;

    public static TimeSpan ComputeDelay(
        int attempt,
        int initialDelayMilliseconds,
        int maxDelayMilliseconds,
        Random? random = null)
    {
        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "attempt debe ser >= 1.");
        if (initialDelayMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialDelayMilliseconds));
        if (maxDelayMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDelayMilliseconds));
        if (initialDelayMilliseconds > maxDelayMilliseconds)
            throw new ArgumentOutOfRangeException(
                nameof(initialDelayMilliseconds),
                "initialDelayMilliseconds debe ser <= maxDelayMilliseconds.");

        var exponent = Math.Min(attempt - 1, MaxExponent);
        var exponential = (double)initialDelayMilliseconds * Math.Pow(2, exponent);
        var baseDelay = (int)Math.Min(maxDelayMilliseconds, exponential);

        var rng = random ?? Random.Shared;
        var jitterBudget = (int)Math.Floor(baseDelay * JitterFraction);
        var remainingBudget = maxDelayMilliseconds - baseDelay;
        var jitterCap = Math.Min(jitterBudget, remainingBudget);
        var jitter = jitterCap <= 0 ? 0 : rng.Next(0, jitterCap + 1);

        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }
}
