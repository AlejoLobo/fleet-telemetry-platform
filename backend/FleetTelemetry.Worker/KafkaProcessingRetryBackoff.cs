namespace FleetTelemetry.Worker;

// Cálculo de backoff exponencial con jitter para reintentos del mismo offset Kafka.
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
        if (maxDelayMilliseconds < initialDelayMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(maxDelayMilliseconds));

        var exponent = Math.Min(attempt - 1, 30);
        var exponential = initialDelayMilliseconds * Math.Pow(2, exponent);
        var capped = (int)Math.Min(maxDelayMilliseconds, exponential);

        // Jitter uniforme en [0, capped/4] para evitar thundering herd.
        var rng = random ?? Random.Shared;
        var jitter = rng.Next(0, Math.Max(1, capped / 4 + 1));
        return TimeSpan.FromMilliseconds(capped + jitter);
    }
}
