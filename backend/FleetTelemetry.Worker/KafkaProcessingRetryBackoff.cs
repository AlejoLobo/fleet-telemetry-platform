namespace FleetTelemetry.Worker;

// Cálculo de backoff exponencial con jitter; el resultado final nunca supera maxDelay.
public static class KafkaProcessingRetryBackoff
{
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

        var exponent = Math.Min(attempt - 1, 20);
        var exponential = (double)initialDelayMilliseconds * Math.Pow(2, exponent);
        var baseDelay = (int)Math.Min(maxDelayMilliseconds, exponential);

        // Jitter dentro del presupuesto restante hasta el máximo (nunca excede max).
        var rng = random ?? Random.Shared;
        var jitterBudget = maxDelayMilliseconds - baseDelay;
        var jitter = jitterBudget <= 0 ? 0 : rng.Next(0, jitterBudget + 1);
        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }
}
