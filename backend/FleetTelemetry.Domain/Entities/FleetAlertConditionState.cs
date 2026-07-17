using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

public sealed class FleetAlertConditionState
{
    public Guid DeviceId => _deviceId.Value;
    public string AlertType => _alertType.Value;
    public bool IsActive { get; private set; }
    public DateTimeOffset LastConditionAt { get; private set; }
    public DateTimeOffset? LastAlertAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly DeviceId _deviceId;
    private readonly AlertType _alertType;

    private FleetAlertConditionState(
        DeviceId deviceId,
        AlertType alertType,
        bool isActive,
        DateTimeOffset lastConditionAt,
        DateTimeOffset? lastAlertAt,
        DateTimeOffset updatedAt)
    {
        _deviceId = deviceId;
        _alertType = alertType;
        IsActive = isActive;
        LastConditionAt = lastConditionAt;
        LastAlertAt = lastAlertAt;
        UpdatedAt = updatedAt;
    }

    public string DeviceIdStorage => DeviceId.ToString("D");

    public static FleetAlertConditionState Create(
        Guid deviceId,
        string alertType,
        bool isActive,
        DateTimeOffset lastConditionAt,
        DateTimeOffset? lastAlertAt,
        DateTimeOffset updatedAt)
    {
        return new FleetAlertConditionState(
            ValueObjects.DeviceId.Create(deviceId),
            ValueObjects.AlertType.Create(alertType),
            isActive,
            lastConditionAt,
            lastAlertAt,
            updatedAt);
    }

    public static FleetAlertConditionState FromPersistence(
        Guid deviceId,
        string alertType,
        bool isActive,
        DateTimeOffset lastConditionAt,
        DateTimeOffset? lastAlertAt,
        DateTimeOffset updatedAt) =>
        Create(deviceId, alertType, isActive, lastConditionAt, lastAlertAt, updatedAt);

    public static FleetAlertConditionState FromPersistence(
        string deviceIdStorage,
        string alertType,
        bool isActive,
        DateTimeOffset lastConditionAt,
        DateTimeOffset? lastAlertAt,
        DateTimeOffset updatedAt)
    {
        if (!Guid.TryParse(deviceIdStorage, out var deviceId))
            throw new ArgumentException("DeviceId storage value is not a valid Guid.", nameof(deviceIdStorage));

        return FromPersistence(deviceId, alertType, isActive, lastConditionAt, lastAlertAt, updatedAt);
    }

    public void MarkActive(DateTimeOffset conditionAt, DateTimeOffset? alertAt, DateTimeOffset updatedAt)
    {
        IsActive = true;
        LastConditionAt = conditionAt;
        if (alertAt.HasValue)
            LastAlertAt = alertAt;
        UpdatedAt = updatedAt;
    }

    public void RefreshCondition(DateTimeOffset conditionAt, DateTimeOffset updatedAt)
    {
        LastConditionAt = conditionAt;
        UpdatedAt = updatedAt;
    }

    public void MarkInactive(DateTimeOffset updatedAt)
    {
        IsActive = false;
        UpdatedAt = updatedAt;
    }
}
