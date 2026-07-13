using Confluent.Kafka;

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

    public RealtimeKafkaPushProcessor(FleetSseBroker broker) => _broker = broker;

    public KafkaPushStepResult Process(ConsumeResult<string, string> result)
    {
        try
        {
            var message = FleetRealtimeKafkaMessage.Deserialize(result.Message.Value);
            var streamId = result.Offset.Value;
            var publishResult = _broker.PublishExternal(
                streamId,
                message.EventType,
                message.Payload,
                message.OccurredAt);

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
        catch
        {
            return new KafkaPushStepResult(KafkaPushAction.Retry, result.Offset.Value);
        }
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
