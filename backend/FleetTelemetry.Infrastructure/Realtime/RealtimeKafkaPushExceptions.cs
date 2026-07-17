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

// Metadata Kafka inaccesible o tópico aún no visible: recuperación con backoff (no Faulted).
public sealed class RealtimeTopicMetadataUnavailableException : Exception
{
    public RealtimeTopicMetadataUnavailableException(string message) : base(message)
    {
    }

    public RealtimeTopicMetadataUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

// Metadata OK pero Partitions.Count != 1: fallo permanente → Faulted.
public sealed class RealtimeTopicPartitionCountException : Exception
{
    public RealtimeTopicPartitionCountException(string topic, int actualPartitionCount)
        : base(
            $"El tópico {topic} debe tener exactamente 1 partición para replay SSE global. Particiones actuales: {actualPartitionCount}.")
    {
        Topic = topic;
        ActualPartitionCount = actualPartitionCount;
    }

    public string Topic { get; }

    public int ActualPartitionCount { get; }
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
