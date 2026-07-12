namespace FleetTelemetry.Application.DTOs;

// Payload del evento stream-reset para resync explícito del cliente.
public record SseStreamResetData(
    string Reason,
    long? LatestEventId);
