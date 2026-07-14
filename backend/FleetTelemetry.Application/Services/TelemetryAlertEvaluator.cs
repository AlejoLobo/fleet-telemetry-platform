using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Services;

// Evalúa umbrales y aplica la máquina de estados activa/cooldown (sin estado en memoria).
public static class TelemetryAlertEvaluator
{
    public const string OverspeedAlertType = "overspeed";
    public const string LowFuelAlertType = "low_fuel";
    public const string LowBatteryAlertType = "low_battery";

    // Detecta incumplimientos actuales (sin crear alertas).
    public static IReadOnlyList<AlertConditionObservation> Observe(
        TelemetryEvent telemetryEvent,
        AlertingOptions options)
    {
        var observations = new List<AlertConditionObservation>(3);

        observations.Add(new AlertConditionObservation(
            OverspeedAlertType,
            "critical",
            $"El vehículo {telemetryEvent.VehicleId} superó el límite de velocidad: {telemetryEvent.SpeedKmh:F1} km/h",
            telemetryEvent.SpeedKmh > options.OverspeedThresholdKmh));

        observations.Add(new AlertConditionObservation(
            LowFuelAlertType,
            "warning",
            $"El vehículo {telemetryEvent.VehicleId} tiene combustible bajo: {telemetryEvent.FuelLevelPercent:F1}%",
            telemetryEvent.FuelLevelPercent is { } fuel && fuel < options.LowFuelPercent));

        observations.Add(new AlertConditionObservation(
            LowBatteryAlertType,
            "warning",
            $"El vehículo {telemetryEvent.VehicleId} tiene batería baja: {telemetryEvent.BatteryPercent:F1}%",
            telemetryEvent.BatteryPercent is { } battery && battery < options.LowBatteryPercent));

        return observations;
    }

    // Compatibilidad: evalúa sin estado previo (primera incidencia → emite).
    public static IReadOnlyList<FleetAlert> Evaluate(TelemetryEvent telemetryEvent) =>
        Evaluate(
            telemetryEvent,
            statesByType: null,
            now: DateTimeOffset.UnixEpoch.AddYears(50),
            options: new AlertingOptions()).EmittedAlerts;

    public static AlertEvaluationResult Evaluate(
        TelemetryEvent telemetryEvent,
        IReadOnlyDictionary<string, FleetAlertConditionState>? statesByType,
        DateTimeOffset now,
        AlertingOptions options)
    {
        var cooldown = TimeSpan.FromSeconds(options.CooldownSeconds);
        var emitted = new List<FleetAlert>();
        var upserts = new List<FleetAlertConditionState>();

        foreach (var observation in Observe(telemetryEvent, options))
        {
            FleetAlertConditionState? current = null;
            statesByType?.TryGetValue(observation.AlertType, out current);
            var transition = Transition(
                telemetryEvent.VehicleId,
                observation,
                current,
                now,
                cooldown);

            if (transition.StateToUpsert is not null)
                upserts.Add(transition.StateToUpsert);

            if (transition.EmittedAlert is not null)
                emitted.Add(transition.EmittedAlert);
        }

        return new AlertEvaluationResult(emitted, upserts);
    }

    // Política de cooldown documentada (reloj de LastAlertAt, no de LastConditionAt):
    // - Inactive + breach + (sin LastAlertAt | cooldown vencido) → emitir + activar.
    // - Inactive + breach + cooldown vigente → activar sin emitir (anti-oscilación).
    // - Active + breach + cooldown vigente → solo LastConditionAt.
    // - Active + breach + cooldown vencido → recordatorio + LastAlertAt.
    // - Active + recovered → IsActive=false (IsAcknowledged no participa).
    // - Inactive + normal → sin cambios.
    public static AlertConditionTransition Transition(
        string vehicleId,
        AlertConditionObservation observation,
        FleetAlertConditionState? current,
        DateTimeOffset now,
        TimeSpan cooldown)
    {
        var isActive = current?.IsActive == true;
        var cooldownElapsed = current?.LastAlertAt is null
            || now - current.LastAlertAt.Value >= cooldown;

        if (!observation.IsBreached)
        {
            if (current is null || !isActive)
                return AlertConditionTransition.NoChange;

            current.MarkInactive(now);
            return AlertConditionTransition.StateOnly(current);
        }

        // Condición incumplida.
        if (!isActive)
        {
            if (current is null)
            {
                var created = FleetAlertConditionState.Create(
                    vehicleId,
                    observation.AlertType,
                    isActive: true,
                    lastConditionAt: now,
                    lastAlertAt: now,
                    updatedAt: now);
                return AlertConditionTransition.Emit(
                    CreateAlert(vehicleId, observation, now),
                    created);
            }

            if (cooldownElapsed)
            {
                current.MarkActive(now, alertAt: now, updatedAt: now);
                return AlertConditionTransition.Emit(
                    CreateAlert(vehicleId, observation, now),
                    current);
            }

            // Oscilación tras recuperación dentro del cooldown: activa sin nueva alerta.
            current.MarkActive(now, alertAt: null, updatedAt: now);
            return AlertConditionTransition.StateOnly(current);
        }

        // Activa + incumplimiento.
        // isActive implica current != null.
        current!.RefreshCondition(now, now);
        if (!cooldownElapsed)
            return AlertConditionTransition.StateOnly(current);

        current.MarkActive(now, alertAt: now, updatedAt: now);
        return AlertConditionTransition.Emit(
            CreateAlert(vehicleId, observation, now),
            current);
    }

    private static FleetAlert CreateAlert(
        string vehicleId,
        AlertConditionObservation observation,
        DateTimeOffset now) =>
        CreateAlert(vehicleId, observation.AlertType, observation.Severity, observation.Message, now);

    private static FleetAlert CreateAlert(
        string vehicleId,
        string alertType,
        string severity,
        string message,
        DateTimeOffset createdAt) =>
        FleetAlert.Create(
            Guid.NewGuid(),
            vehicleId,
            alertType,
            severity,
            message,
            createdAt);
}

public sealed class AlertConditionTransition
{
    public static AlertConditionTransition NoChange { get; } = new(null, null);

    private AlertConditionTransition(FleetAlert? emittedAlert, FleetAlertConditionState? stateToUpsert)
    {
        EmittedAlert = emittedAlert;
        StateToUpsert = stateToUpsert;
    }

    public FleetAlert? EmittedAlert { get; }

    public FleetAlertConditionState? StateToUpsert { get; }

    public static AlertConditionTransition Emit(FleetAlert alert, FleetAlertConditionState state) =>
        new(alert, state);

    public static AlertConditionTransition StateOnly(FleetAlertConditionState state) =>
        new(null, state);
}
