// DTO de alerta expuesta por la API.
namespace FleetTelemetry.Application.DTOs;

// Datos de una alerta de flota para lectura.
public record FleetAlertResponse(
    Guid AlertId,
    string VehicleId,
    string AlertType,
    string Severity,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsAcknowledged);
