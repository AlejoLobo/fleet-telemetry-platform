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
    private readonly ILogger<FleetSseKafkaPushHostedService>? _logger;
    private readonly FleetTelemetryMetrics? _metrics;

    public RealtimeKafkaPushProcessor(
        FleetSseBroker broker,
        ILogger<FleetSseKafkaPushHostedService>? logger = null,
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
            var message = FleetRealtimeKafkaMessage.Deserialize(result.Message.Value);
            if (string.IsNullOrWhiteSpace(message.EventType))
            {
                return HandleInvalidPayload(result, "missing-event-type");
            }

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

// Loop secuencial: no confirma offsets posteriores si el actual falló.
internal sealed class FleetRealtimeKafkaPushLoop
{
    private readonly RealtimeKafkaPushProcessor _processor;
    private long? _blockedOffset;

    public FleetRealtimeKafkaPushLoop(RealtimeKafkaPushProcessor processor) =>
        _processor = processor;

    internal long? BlockedOffset => _blockedOffset;

    public void RunOnce(IRealtimeKafkaPushTransport transport, TimeSpan timeout)
    {
        if (_blockedOffset.HasValue)
            transport.Seek(_blockedOffset.Value);

        var result = transport.Consume(timeout);
        if (result?.Message?.Value is null)
            return;

        var offset = result.Offset.Value;
        if (_blockedOffset.HasValue && offset > _blockedOffset.Value)
        {
            transport.Seek(_blockedOffset.Value);
            return;
        }

        var step = _processor.Process(result);
        if (step.Action == KafkaPushAction.Commit)
        {
            transport.Commit(result);
            if (_blockedOffset == offset)
                _blockedOffset = null;
            return;
        }

        _blockedOffset = offset;
        transport.Seek(offset);
    }
}
