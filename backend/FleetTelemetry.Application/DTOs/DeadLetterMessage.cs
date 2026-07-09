namespace FleetTelemetry.Application.DTOs;

// Mensaje enviado al tópico DLQ cuando el procesamiento falla de forma definitiva.
public record DeadLetterMessage(
    string SourceTopic,
    int Partition,
    long Offset,
    string? OriginalKey,
    string OriginalPayload,
    string FailureReason,
    string FailureType,
    int AttemptCount,
    DateTimeOffset FailedAt);
