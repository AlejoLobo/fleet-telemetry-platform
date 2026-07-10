namespace FleetTelemetry.Application.DTOs;

// Mensaje publicado en telemetry.dead-letter cuando el procesamiento falla de forma definitiva.
public record DeadLetterMessage(
    string OriginalPayload,
    string Reason,
    string ExceptionMessage,
    string OriginalTopic,
    int Partition,
    long Offset,
    DateTimeOffset OccurredAt);
