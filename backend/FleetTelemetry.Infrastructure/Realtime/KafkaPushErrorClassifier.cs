using Confluent.Kafka;

namespace FleetTelemetry.Infrastructure.Realtime;

// Clasifica fallos pre-poll / materialización: transient → backoff; permanent → Faulted.
internal static class KafkaPushErrorClassifier
{
    public static bool IsPermanent(Exception exception)
    {
        // ConsumeException deriva de KafkaException: un solo arm IsFatal basta.
        return exception switch
        {
            RealtimeTopicPartitionCountException => true,
            ArgumentException => true,
            InvalidCastException => true,
            NotSupportedException => true,
            NotImplementedException => true,
            KafkaException { Error.IsFatal: true } => true,
            _ => false
        };
    }

    public static bool IsTransient(Exception exception) => !IsPermanent(exception);

    public static string ResolveFailedPhase(string currentPhase, Exception exception) =>
        exception switch
        {
            RealtimeKafkaAssignmentMaterializationException => "MaterializeAssignment",
            RealtimeTopicMetadataUnavailableException => "ValidateTopic",
            RealtimeTopicPartitionCountException => "ValidateTopic",
            _ => currentPhase
        };
}
