using Confluent.Kafka;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Realtime;

// Acciones del loop Kafka→SSE ante un mensaje consumido.
internal enum KafkaPushAction
{
    Commit,
    Retry
}

// Resultado de procesar un mensaje Kafka antes de confirmar offset.
internal sealed record KafkaPushStepResult(KafkaPushAction Action, long Offset);

// Abstracción del consumidor Kafka para pruebas del loop de push SSE.
internal interface IRealtimeKafkaPushTransport
{
    ConsumeResult<string, string>? Consume(TimeSpan timeout);

    void Commit(ConsumeResult<string, string> result);

    void Seek(long offset);
}

// Transporte Confluent.Kafka usado en producción por el hosted service.
internal sealed class KafkaRealtimePushTransport : IRealtimeKafkaPushTransport, IDisposable
{
    private readonly IConsumer<string, string> _consumer;
    private readonly string _topic;

    public KafkaRealtimePushTransport(IConsumer<string, string> consumer, string topic)
    {
        _consumer = consumer;
        _topic = topic;
    }

    public ConsumeResult<string, string>? Consume(TimeSpan timeout) =>
        _consumer.Consume(timeout);

    public void Commit(ConsumeResult<string, string> result) =>
        _consumer.Commit(result);

    public void Seek(long offset) =>
        _consumer.Seek(new TopicPartitionOffset(_topic, 0, offset));

    public void Dispose() => _consumer.Dispose();
}

// Procesa payloads Kafka y publica en el broker SSE con resultado tipado.
internal sealed class RealtimeKafkaPushProcessor
{
    private readonly FleetSseBroker _broker;
    private readonly ILogger? _logger;
    private readonly FleetTelemetryMetrics? _metrics;

    public RealtimeKafkaPushProcessor(
        FleetSseBroker broker,
        ILogger? logger = null,
        FleetTelemetryMetrics? metrics = null)
    {
        _broker = broker;
        _logger = logger;
        _metrics = metrics;
    }

    public KafkaPushStepResult Process(ConsumeResult<string, string> result)
    {
        try
        {
            if (result.Message is null || result.Message.Value is null)
            {
                return HandleInvalidPayload(result, "tombstone-or-null-value");
            }

            var message = FleetRealtimeKafkaMessage.Deserialize(result.Message.Value);
            FleetRealtimeKafkaMessageValidator.Validate(message);

            var streamId = result.Offset.Value;
            Application.Realtime.ExternalPublishResult publishResult;
            try
            {
                publishResult = _broker.PublishExternal(
                    streamId,
                    message.EventType,
                    message.Payload,
                    message.OccurredAt);
            }
            catch (Exception ex)
            {
                throw new RealtimeKafkaTransientPublishException(
                    $"Transient SSE publish failure at offset {streamId}.",
                    ex);
            }

            return publishResult switch
            {
                Application.Realtime.ExternalPublishResult.Accepted =>
                    new KafkaPushStepResult(KafkaPushAction.Commit, streamId),
                Application.Realtime.ExternalPublishResult.Duplicate =>
                    new KafkaPushStepResult(KafkaPushAction.Commit, streamId),
                Application.Realtime.ExternalPublishResult.OutOfOrder =>
                    new KafkaPushStepResult(KafkaPushAction.Retry, streamId),
                _ => new KafkaPushStepResult(KafkaPushAction.Retry, streamId)
            };
        }
        catch (RealtimeKafkaInvalidPayloadException ex)
        {
            return HandleInvalidPayload(result, ex.Message);
        }
        catch (RealtimeKafkaTransientPublishException)
        {
            return new KafkaPushStepResult(KafkaPushAction.Retry, result.Offset.Value);
        }
    }

    private KafkaPushStepResult HandleInvalidPayload(ConsumeResult<string, string> result, string reason)
    {
        var offset = result.Offset.Value;
        _logger?.LogError(
            "Invalid fleet.realtime payload at {Topic}/{Partition}@{Offset}: {Reason}",
            result.Topic,
            result.Partition.Value,
            offset,
            reason);

        _metrics?.RealtimeInvalidPayloadTotal.Add(1);

        _broker.RecordInvalidExternalOffset(offset);
        _broker.PublishStreamResetToAll("invalid-payload");

        return new KafkaPushStepResult(KafkaPushAction.Commit, offset);
    }
}

// Loop secuencial con recuperación de Commit/Seek y backoff cancelable.
internal sealed class FleetRealtimeKafkaPushLoop
{
    private readonly RealtimeKafkaPushProcessor _processor;
    private readonly ILogger? _logger;
    private readonly FleetTelemetryMetrics? _metrics;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _backoff;
    private long? _blockedOffset;
    private int _transportFailureStreak;

    public FleetRealtimeKafkaPushLoop(
        RealtimeKafkaPushProcessor processor,
        ILogger? logger = null,
        FleetTelemetryMetrics? metrics = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? backoff = null)
    {
        _processor = processor;
        _logger = logger;
        _metrics = metrics;
        _delayAsync = delayAsync ?? ((delay, ct) => Task.Delay(delay, ct));
        _backoff = backoff ?? TimeSpan.FromMilliseconds(200);
    }

    internal long? BlockedOffset => _blockedOffset;

    public void RunOnce(IRealtimeKafkaPushTransport transport, TimeSpan timeout) =>
        RunOnceAsync(transport, timeout, CancellationToken.None).GetAwaiter().GetResult();

    public async Task RunOnceAsync(
        IRealtimeKafkaPushTransport transport,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_blockedOffset.HasValue)
            {
                if (!TrySeek(transport, _blockedOffset.Value))
                {
                    await DelayBackoffAsync(cancellationToken);
                    return;
                }
            }

            var result = transport.Consume(timeout);
            if (result is null)
                return;

            var offset = result.Offset.Value;
            if (_blockedOffset.HasValue && offset > _blockedOffset.Value)
            {
                if (!TrySeek(transport, _blockedOffset.Value))
                    await DelayBackoffAsync(cancellationToken);
                return;
            }

            var step = _processor.Process(result);
            if (step.Action == KafkaPushAction.Commit)
            {
                if (!TryCommit(transport, result))
                {
                    _blockedOffset = offset;
                    await DelayBackoffAsync(cancellationToken);
                    return;
                }

                if (_blockedOffset == offset)
                    _blockedOffset = null;
                _transportFailureStreak = 0;
                return;
            }

            // Retry: mantener bloqueado, Seek y backoff aunque Seek sea exitoso.
            _blockedOffset = offset;
            _transportFailureStreak++;
            _metrics?.RealtimePublishFailuresTotal.Add(1);
            _logger?.LogWarning(
                "Transient Kafka publish failure at {Topic}/{Partition}@{Offset}; will retry",
                result.Topic,
                result.Partition.Value,
                offset);
            _ = TrySeek(transport, offset);
            await DelayBackoffAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected Kafka push loop failure");
            await DelayBackoffAsync(cancellationToken);
        }
    }

    private bool TryCommit(IRealtimeKafkaPushTransport transport, ConsumeResult<string, string> result)
    {
        try
        {
            transport.Commit(result);
            return true;
        }
        catch (Exception ex)
        {
            _transportFailureStreak++;
            _metrics?.RealtimeCommitFailuresTotal.Add(1);
            _logger?.LogError(
                ex,
                "Kafka commit failed at {Topic}/{Partition}@{Offset}",
                result.Topic,
                result.Partition.Value,
                result.Offset.Value);
            return false;
        }
    }

    private bool TrySeek(IRealtimeKafkaPushTransport transport, long offset)
    {
        try
        {
            transport.Seek(offset);
            return true;
        }
        catch (Exception ex)
        {
            _transportFailureStreak++;
            _metrics?.RealtimeSeekFailuresTotal.Add(1);
            _logger?.LogError(ex, "Kafka seek failed at offset {Offset}", offset);
            return false;
        }
    }

    private Task DelayBackoffAsync(CancellationToken cancellationToken)
    {
        var factor = Math.Min(8, Math.Max(1, _transportFailureStreak));
        var delay = TimeSpan.FromMilliseconds(_backoff.TotalMilliseconds * factor);
        return _delayAsync(delay, cancellationToken);
    }
}
