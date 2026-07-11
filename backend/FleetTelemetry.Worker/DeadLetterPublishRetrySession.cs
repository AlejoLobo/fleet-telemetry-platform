using FleetTelemetry.Application.Exceptions;

namespace FleetTelemetry.Worker;

// Controla reintentos exclusivos de publicación DLQ sin confirmar offset.
public sealed class DeadLetterPublishRetrySession
{
    private readonly int _maxAttempts;
    private readonly int _initialDelayMilliseconds;
    private readonly int _maxDelayMilliseconds;
    private int _attempt;

    public DeadLetterPublishRetrySession(
        int maxAttempts,
        int initialDelayMilliseconds,
        int maxDelayMilliseconds)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        _maxAttempts = maxAttempts;
        _initialDelayMilliseconds = initialDelayMilliseconds;
        _maxDelayMilliseconds = maxDelayMilliseconds;
    }

    public DeadLetterPublishRetryDecision RegisterPublishFailure(
        DeadLetterPublishException exception,
        string topic,
        int partition,
        long offset)
    {
        _ = exception;
        _ = topic;
        _ = partition;
        _ = offset;

        _attempt++;
        if (_attempt >= _maxAttempts)
        {
            return new DeadLetterPublishRetryDecision(
                Attempt: _attempt,
                ShouldStopWorker: true,
                Delay: TimeSpan.Zero);
        }

        var delay = KafkaProcessingRetryBackoff.ComputeDelay(
            _attempt,
            _initialDelayMilliseconds,
            _maxDelayMilliseconds);

        return new DeadLetterPublishRetryDecision(
            Attempt: _attempt,
            ShouldStopWorker: false,
            Delay: delay);
    }
}

public readonly record struct DeadLetterPublishRetryDecision(
    int Attempt,
    bool ShouldStopWorker,
    TimeSpan Delay);
