// Entidad de alerta operativa de la flota.
namespace FleetTelemetry.Domain.Entities;

// Representa una alerta generada por telemetría.
public class FleetAlert
{
    public Guid AlertId { get; set; }
    public string VehicleId { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsAcknowledged { get; set; }
}
