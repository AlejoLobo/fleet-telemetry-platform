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

// Assign no confirmó la posición objetivo a tiempo; recrear sesión (no Faulted).
public sealed class RealtimeKafkaAssignmentMaterializationException : Exception
{
    public RealtimeKafkaAssignmentMaterializationException(
        string topic,
        int partition,
        long targetOffset,
        string? message = null,
        Exception? inner = null)
        : base(
            message
            ?? $"Kafka Assign materialization timed out. Topic={topic} Partition={partition} TargetOffset={targetOffset}",
            inner)
    {
        Topic = topic;
        Partition = partition;
        TargetOffset = targetOffset;
    }

    public string Topic { get; }

    public int Partition { get; }

    public long TargetOffset { get; }
}
