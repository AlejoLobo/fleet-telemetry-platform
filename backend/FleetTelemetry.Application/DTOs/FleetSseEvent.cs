// DTO de evento en tiempo real vía SSE.
namespace FleetTelemetry.Application.DTOs;

// Evento push con identificador monotónico, tipo, carga y marca temporal.
public record FleetSseEvent(
    long StreamId,
    string EventType,
    object Data,
    DateTimeOffset Timestamp);
