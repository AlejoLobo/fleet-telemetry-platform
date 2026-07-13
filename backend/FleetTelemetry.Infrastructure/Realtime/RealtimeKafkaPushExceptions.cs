namespace FleetTelemetry.Infrastructure.Realtime;

// Payload Kafka inmutable inválido: avanzar offset sin reintentar indefinidamente.
public sealed class RealtimeKafkaInvalidPayloadException : Exception
{
    public RealtimeKafkaInvalidPayloadException(string message) : base(message)
    {
    }

    public RealtimeKafkaInvalidPayloadException(string message, Exception inner) : base(message, inner)
    {
    }
}

// Fallo transitorio al publicar en el broker SSE: reintentar el mismo offset.
public sealed class RealtimeKafkaTransientPublishException : Exception
{
    public RealtimeKafkaTransientPublishException(string message, Exception inner) : base(message, inner)
    {
    }
}
