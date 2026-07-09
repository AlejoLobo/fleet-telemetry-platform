// DTO de evento en tiempo real vía SSE.
namespace FleetTelemetry.Application.DTOs;

// Evento push con tipo, carga y marca temporal.
public record FleetSseEvent(
    string EventType,
    object Data,
    DateTimeOffset Timestamp);
