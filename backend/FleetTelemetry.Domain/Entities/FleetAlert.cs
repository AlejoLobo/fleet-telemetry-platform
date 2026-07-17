using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

public sealed class FleetAlert
{
    public Guid AlertId => _alertId;
    public Guid DeviceId => _deviceId.Value;
    public string AlertType => _alertType.Value;
    public string Severity => _severity.Value;
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsAcknowledged { get; private set; }

    private readonly Guid _alertId;
    private readonly DeviceId _deviceId;
    private readonly AlertType _alertType;
    private readonly AlertSeverity _severity;

    private FleetAlert(
        Guid alertId,
        DeviceId deviceId,
        AlertType alertType,
        AlertSeverity severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged)
    {
        _alertId = alertId;
        _deviceId = deviceId;
        _alertType = alertType;
        _severity = severity;
        Message = message;
        CreatedAt = createdAt;
        IsAcknowledged = isAcknowledged;
    }

    public string DeviceIdStorage => DeviceId.ToString("D");

    public static bool TryCreate(
        Guid alertId,
        Guid deviceId,
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

        if (!ValueObjects.DeviceId.TryCreate(deviceId, out var domainDeviceId, out error))
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
            domainDeviceId!,
            domainAlertType!,
            domainSeverity!,
            message.Trim(),
            createdAt,
            isAcknowledged);

        return true;
    }

    public static FleetAlert Create(
        Guid alertId,
        Guid deviceId,
        string alertType,
        string severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged = false) =>
        TryCreate(
            alertId,
            deviceId,
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
        Guid deviceId,
        string alertType,
        string severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged) =>
        new(
            alertId,
            ValueObjects.DeviceId.Create(deviceId),
            ValueObjects.AlertType.Create(alertType),
            ValueObjects.AlertSeverity.Create(severity),
            message,
            createdAt,
            isAcknowledged);

    public static FleetAlert FromPersistence(
        Guid alertId,
        string deviceIdStorage,
        string alertType,
        string severity,
        string message,
        DateTimeOffset createdAt,
        bool isAcknowledged)
    {
        if (!Guid.TryParse(deviceIdStorage, out var deviceId))
            throw new ArgumentException("DeviceId storage value is not a valid Guid.", nameof(deviceIdStorage));

        return FromPersistence(
            alertId,
            deviceId,
            alertType,
            severity,
            message,
            createdAt,
            isAcknowledged);
    }
}
