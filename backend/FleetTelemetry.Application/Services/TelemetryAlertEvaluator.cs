using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Services;

public static class TelemetryAlertEvaluator
{
    private const double CriticalSpeedKmh = 120;
    private const double LowFuelPercent = 15;
    private const double LowBatteryPercent = 20;

    public static IReadOnlyList<FleetAlert> Evaluate(TelemetryEvent telemetryEvent)
    {
        var alerts = new List<FleetAlert>();

        if (telemetryEvent.SpeedKmh > CriticalSpeedKmh)
        {
            alerts.Add(CreateAlert(
                telemetryEvent.VehicleId,
                "overspeed",
                "critical",
                $"Vehicle {telemetryEvent.VehicleId} exceeded speed limit: {telemetryEvent.SpeedKmh:F1} km/h"));
        }

        if (telemetryEvent.FuelLevelPercent is < LowFuelPercent)
        {
            alerts.Add(CreateAlert(
                telemetryEvent.VehicleId,
                "low_fuel",
                "warning",
                $"Vehicle {telemetryEvent.VehicleId} has low fuel: {telemetryEvent.FuelLevelPercent:F1}%"));
        }

        if (telemetryEvent.BatteryPercent is < LowBatteryPercent)
        {
            alerts.Add(CreateAlert(
                telemetryEvent.VehicleId,
                "low_battery",
                "warning",
                $"Vehicle {telemetryEvent.VehicleId} has low battery: {telemetryEvent.BatteryPercent:F1}%"));
        }

        return alerts;
    }

    private static FleetAlert CreateAlert(
        string vehicleId,
        string alertType,
        string severity,
        string message) => new()
    {
        AlertId = Guid.NewGuid(),
        VehicleId = vehicleId,
        AlertType = alertType,
        Severity = severity,
        Message = message,
        CreatedAt = DateTimeOffset.UtcNow,
        IsAcknowledged = false
    };
}
