// DTO de ingesta masiva de telemetría.
namespace FleetTelemetry.Application.DTOs;

// Lote de eventos a publicar en Kafka.
public record TelemetryBatchRequest(
    IReadOnlyList<TelemetryEventRequest> Events);
