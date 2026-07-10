namespace FleetTelemetry.Worker;

// Contexto mínimo de un mensaje consumido (sin acoplar a IConsumer).
public sealed record KafkaConsumedMessage(
    string Payload,
    string Topic,
    int Partition,
    long Offset,
    string? Key = null);
