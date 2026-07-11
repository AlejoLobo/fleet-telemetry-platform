using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

// Alerta operativa de flota con severidad y tipo validados.
public sealed class FleetAlert
{
    public Guid AlertId => _alertId;
    public string VehicleId => _vehicleId.Value;
    public string AlertType => _alertType.Value;
    public string Severity => _severity.Value;
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsAcknowledged { get; private set; }

    private readonly Guid _alertId;
    private readonly VehicleId _vehicleId;
    private readonly AlertType _alertType;
    private readonly AlertSeverity _severity;

    private FleetAlert(
        Guid alertId,
        VehicleId vehicleId,
        AlertType alertType,
        AlertSeverity severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged)
    {
        _alertId = alertId;
        _vehicleId = vehicleId;
        _alertType = alertType;
        _severity = severity;
        Message = message;
        CreatedAt = createdAt;
        IsAcknowledged = isAcknowledged;
    }

    public static bool TryCreate(
        Guid alertId,
        string vehicleId,
        string alertType,
        string severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged,
        out FleetAlert? alert,
        out string? error)
    {
        alert = null;
        error = null;

        if (alertId == Guid.Empty)
        {
            error = "AlertId is required.";
            return false;
        }

        if (!ValueObjects.VehicleId.TryCreate(vehicleId, out var domainVehicleId, out error))
            return false;

        if (!ValueObjects.AlertType.TryCreate(alertType, out var domainAlertType, out error))
            return false;

        if (!ValueObjects.AlertSeverity.TryCreate(severity, out var domainSeverity, out error))
            return false;

        if (string.IsNullOrWhiteSpace(message))
        {
            error = "Alert message is required.";
            return false;
        }

        if (createdAt == default)
        {
            error = "CreatedAt is required.";
            return false;
        }

        alert = new FleetAlert(
            alertId,
            domainVehicleId!,
            domainAlertType!,
            domainSeverity!,
            message.Trim(),
            createdAt,
            isAcknowledged);

        return true;
    }

    public static FleetAlert Create(
        Guid alertId,
        string vehicleId,
        string alertType,
        string severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged = false) =>
        TryCreate(
            alertId,
            vehicleId,
            alertType,
            severity,
            message,
            createdAt,
            isAcknowledged,
            out var alert,
            out var error)
            ? alert!
            : throw new ArgumentException(error);

    public void Acknowledge()
    {
        if (IsAcknowledged)
            return;

        IsAcknowledged = true;
    }

    public static FleetAlert FromPersistence(
        Guid alertId,
        string vehicleId,
        string alertType,
        string severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged) =>
        new(
            alertId,
            ValueObjects.VehicleId.Create(vehicleId),
            ValueObjects.AlertType.Create(alertType),
            ValueObjects.AlertSeverity.Create(severity),
            message,
            createdAt,
            isAcknowledged);
}
