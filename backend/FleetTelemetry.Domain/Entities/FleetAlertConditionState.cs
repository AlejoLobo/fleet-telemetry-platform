using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

// Estado persistente de condición de alerta por vehículo y tipo (no es IsAcknowledged).
public sealed class FleetAlertConditionState
{
    public string VehicleId => _vehicleId.Value;
    public string AlertType => _alertType.Value;
    public bool IsActive { get; private set; }
    public DateTimeOffset LastConditionAt { get; private set; }
    public DateTimeOffset? LastAlertAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly VehicleId _vehicleId;
    private readonly AlertType _alertType;

    private FleetAlertConditionState(
        VehicleId vehicleId,
        AlertType alertType,
        bool isActive,
        DateTimeOffset lastConditionAt,
        DateTimeOffset? lastAlertAt,
        DateTimeOffset updatedAt)
    {
        _vehicleId = vehicleId;
        _alertType = alertType;
        IsActive = isActive;
        LastConditionAt = lastConditionAt;
        LastAlertAt = lastAlertAt;
        UpdatedAt = updatedAt;
    }

    public static FleetAlertConditionState Create(
        string vehicleId,
        string alertType,
        bool isActive,
        DateTimeOffset lastConditionAt,
        DateTimeOffset? lastAlertAt,
        DateTimeOffset updatedAt)
    {
        return new FleetAlertConditionState(
            ValueObjects.VehicleId.Create(vehicleId),
            ValueObjects.AlertType.Create(alertType),
            isActive,
            lastConditionAt,
            lastAlertAt,
            updatedAt);
    }

    public static FleetAlertConditionState FromPersistence(
        string vehicleId,
        string alertType,
        bool isActive,
        DateTimeOffset lastConditionAt,
        DateTimeOffset? lastAlertAt,
        DateTimeOffset updatedAt) =>
        Create(vehicleId, alertType, isActive, lastConditionAt, lastAlertAt, updatedAt);

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
