namespace FleetTelemetry.Application.DTOs;

public record FleetAlertResponse(
    Guid AlertId,
    string VehicleId,
    string AlertType,
    string Severity,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsAcknowledged);
