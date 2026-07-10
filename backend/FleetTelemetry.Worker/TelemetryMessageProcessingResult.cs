namespace FleetTelemetry.Worker;

// Resultado del procesamiento de un mensaje Kafka (el Worker decide el commit).
public enum TelemetryMessageProcessingResult
{
    ProcessedAndCommit,
    SentToDeadLetterAndCommit,
    RetryWithoutCommit,
    IgnoreWithoutCommit
}
