using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Worker;

// Resultado de procesamiento con mensaje DLQ pendiente (publicación separada en el coordinador).
public sealed record TelemetryMessageProcessingOutcome(
    TelemetryMessageProcessingResult Result,
    DeadLetterMessage? PendingDeadLetter = null);
