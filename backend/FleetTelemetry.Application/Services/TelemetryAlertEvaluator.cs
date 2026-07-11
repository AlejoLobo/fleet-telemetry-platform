using FleetTelemetry.Domain.Entities;

// Evaluación de reglas de alerta sobre telemetría.
namespace FleetTelemetry.Application.Services;

// Genera alertas por exceso de velocidad, combustible o batería bajos.
public static class TelemetryAlertEvaluator
{
    private const double CriticalSpeedKmh = 120;
    private const double LowFuelPercent = 15;
    private const double LowBatteryPercent = 20;

    // Evalúa umbrales y devuelve alertas detectadas.
    public static IReadOnlyList<FleetAlert> Evaluate(TelemetryEvent telemetryEvent)
    {
        var alerts = new List<FleetAlert>();

        if (telemetryEvent.SpeedKmh > CriticalSpeedKmh)
        {
            alerts.Add(CreateAlert(
                telemetryEvent.VehicleId,
                "overspeed",
                "critical",
                $"El vehículo {telemetryEvent.VehicleId} superó el límite de velocidad: {telemetryEvent.SpeedKmh:F1} km/h"));
        }

        if (telemetryEvent.FuelLevelPercent is < LowFuelPercent)
        {
            alerts.Add(CreateAlert(
                telemetryEvent.VehicleId,
                "low_fuel",
                "warning",
                $"El vehículo {telemetryEvent.VehicleId} tiene combustible bajo: {telemetryEvent.FuelLevelPercent:F1}%"));
        }

        if (telemetryEvent.BatteryPercent is < LowBatteryPercent)
        {
            alerts.Add(CreateAlert(
                telemetryEvent.VehicleId,
                "low_battery",
                "warning",
                $"El vehículo {telemetryEvent.VehicleId} tiene batería baja: {telemetryEvent.BatteryPercent:F1}%"));
        }

        return alerts;
    }

    // Construye entidad de alerta con valores por defecto.
    private static FleetAlert CreateAlert(
        string vehicleId,
        string alertType,
        string severity,
        string message) =>
        FleetAlert.Create(
            Guid.NewGuid(),
            vehicleId,
            alertType,
            severity,
            message,
            DateTimeOffset.UtcNow);
}
