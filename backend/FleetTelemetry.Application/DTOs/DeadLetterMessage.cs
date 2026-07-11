namespace FleetTelemetry.Application.DTOs;

// Mensaje enriquecido publicado en telemetry.dead-letter.
public record DeadLetterMessage(
    Guid DeadLetterId,
    int SchemaVersion,
    string Category,
    string ErrorCode,
    int AttemptNumber,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ProcessedAt,
    string OriginalTopic,
    int Partition,
    long Offset,
    string? MessageKey,
    Guid? CorrelationId,
    string OriginalPayload,
    string TechnicalDetail,
    string Reason,
    string ExceptionMessage);
