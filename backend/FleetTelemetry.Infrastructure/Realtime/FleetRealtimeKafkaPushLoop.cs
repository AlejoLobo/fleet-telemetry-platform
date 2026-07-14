using Confluent.Kafka;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Realtime;

// Resultado de un ciclo del loop con pendingRecord único.
internal enum KafkaPushPollResult
{
    Idle = 0,
    Completed = 1,
    TransientFailure = 2,
    FatalFailure = 3
}

// Resultado de procesar el registro pendiente (sin Commit/Seek Kafka).
internal enum PendingProcessOutcome
{
    Completed = 0,
    TransientFailure = 1
}

// Abstracción del consumidor con Assign manual (sin Subscribe/group).
internal interface IRealtimeKafkaPushTransport
{
    ConsumeResult<string, string>? Consume(TimeSpan timeout);
}

// Transporte Confluent.Kafka con posición fija vía Assign.
// Preferir IRealtimeKafkaConsumerSession; se mantiene por compatibilidad de tests unitarios del loop.
internal sealed class KafkaRealtimePushTransport : IRealtimeKafkaPushTransport, IDisposable
{
    private readonly IConsumer<string, string> _consumer;

    public KafkaRealtimePushTransport(IConsumer<string, string> consumer)
    {
        _consumer = consumer;
    }

    public ConsumeResult<string, string>? Consume(TimeSpan timeout) =>
        _consumer.Consume(timeout);

    public void Dispose() => _consumer.Dispose();
}

// Procesa un ConsumeResult ya leído: publica o marca inválido; nunca Seek.
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

    public PendingProcessOutcome Process(ConsumeResult<string, string> result)
    {
        try
        {
            if (result.Message is null || result.Message.Value is null)
                return CompleteInvalid(result, "tombstone-or-null-value");

            var message = FleetRealtimeKafkaMessage.Deserialize(result.Message.Value);
            FleetRealtimeKafkaMessageValidator.Validate(message);

            var streamId = result.Offset.Value;
            ExternalPublishResult publishResult;
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
                ExternalPublishResult.Accepted => PendingProcessOutcome.Completed,
                ExternalPublishResult.Duplicate => PendingProcessOutcome.Completed,
                // Fuera de secuencia inesperada: reintentar el mismo registro (sin Seek).
                ExternalPublishResult.OutOfOrder => PendingProcessOutcome.TransientFailure,
                _ => PendingProcessOutcome.TransientFailure
            };
        }
        catch (RealtimeKafkaInvalidPayloadException ex)
        {
            return CompleteInvalid(result, ex.Message);
        }
        catch (RealtimeKafkaTransientPublishException)
        {
            _metrics?.RealtimePublishFailuresTotal.Add(1);
            _logger?.LogWarning(
                "Transient Kafka publish failure at {Topic}/{Partition}@{Offset}; will retry same record",
                result.Topic,
                result.Partition.Value,
                result.Offset.Value);
            return PendingProcessOutcome.TransientFailure;
        }
    }

    private PendingProcessOutcome CompleteInvalid(ConsumeResult<string, string> result, string reason)
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
        return PendingProcessOutcome.Completed;
    }
}

// Loop con un único pendingRecord: no Consume N+1 hasta completar el pendiente.
internal sealed class FleetRealtimeKafkaPushLoop
{
    private readonly RealtimeKafkaPushProcessor _processor;
    private readonly ILogger? _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _backoff;
    private ConsumeResult<string, string>? _pendingRecord;
    private int _failureStreak;

    public FleetRealtimeKafkaPushLoop(
        RealtimeKafkaPushProcessor processor,
        ILogger? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? backoff = null)
    {
        _processor = processor;
        _logger = logger;
        _delayAsync = delayAsync ?? ((delay, ct) => Task.Delay(delay, ct));
        _backoff = backoff ?? TimeSpan.FromMilliseconds(200);
    }

    internal ConsumeResult<string, string>? PendingRecord => _pendingRecord;

    public void AbandonPending() => _pendingRecord = null;

    public KafkaPushPollResult RunOnce(IRealtimeKafkaPushTransport transport, TimeSpan timeout) =>
        RunOnceAsync(transport, timeout, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<KafkaPushPollResult> RunOnceAsync(
        IRealtimeKafkaPushTransport transport,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_pendingRecord is null)
            {
                var consumed = transport.Consume(timeout);
                if (consumed is null)
                    return KafkaPushPollResult.Idle;

                _pendingRecord = consumed;
            }

            var outcome = _processor.Process(_pendingRecord);
            if (outcome == PendingProcessOutcome.Completed)
            {
                _pendingRecord = null;
                _failureStreak = 0;
                return KafkaPushPollResult.Completed;
            }

            _failureStreak++;
            await DelayBackoffAsync(cancellationToken);
            return KafkaPushPollResult.TransientFailure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConsumeException ex) when (ex.Error.IsFatal)
        {
            _logger?.LogCritical(ex, "Fatal Kafka consume error in push loop");
            return KafkaPushPollResult.FatalFailure;
        }
        catch (ConsumeException ex)
        {
            _logger?.LogError(ex, "Transient Kafka consume error. Reason={Reason}", ex.Error.Reason);
            _failureStreak++;
            await DelayBackoffAsync(cancellationToken);
            return KafkaPushPollResult.TransientFailure;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected Kafka push loop failure");
            _failureStreak++;
            await DelayBackoffAsync(cancellationToken);
            return KafkaPushPollResult.TransientFailure;
        }
    }

    private Task DelayBackoffAsync(CancellationToken cancellationToken)
    {
        var factor = Math.Min(8, Math.Max(1, _failureStreak));
        var delay = TimeSpan.FromMilliseconds(_backoff.TotalMilliseconds * factor);
        return _delayAsync(delay, cancellationToken);
    }
}
