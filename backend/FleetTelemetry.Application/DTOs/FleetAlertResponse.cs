namespace FleetTelemetry.Application.DTOs;

public record FleetAlertResponse(
    Guid AlertId,
    Guid DeviceId,
    string AlertType,
    string Severity,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsAcknowledged);
