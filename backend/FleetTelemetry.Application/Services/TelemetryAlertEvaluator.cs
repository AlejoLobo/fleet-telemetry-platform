using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Services;

public static class TelemetryAlertEvaluator
{
    public const string OverspeedAlertType = "overspeed";
    public const string LowFuelAlertType = "low_fuel";
    public const string LowBatteryAlertType = "low_battery";

    public static IReadOnlyList<AlertConditionObservation> Observe(
        TelemetryEvent telemetryEvent,
        AlertingOptions options)
    {
        var observations = new List<AlertConditionObservation>(3);

        observations.Add(new AlertConditionObservation(
            OverspeedAlertType,
            "critical",
            $"El vehículo {telemetryEvent.DeviceId:D} superó el límite de velocidad: {telemetryEvent.SpeedKmh:F1} km/h",
            telemetryEvent.SpeedKmh > options.OverspeedThresholdKmh
                ? AlertConditionObservationStatus.Breached
                : AlertConditionObservationStatus.Recovered));

        observations.Add(new AlertConditionObservation(
            LowFuelAlertType,
            "warning",
            $"El vehículo {telemetryEvent.DeviceId:D} tiene combustible bajo: {telemetryEvent.FuelLevelPercent:F1}%",
            ResolveNullableThreshold(
                telemetryEvent.FuelLevelPercent,
                options.LowFuelPercent,
                breachedWhenBelow: true)));

        observations.Add(new AlertConditionObservation(
            LowBatteryAlertType,
            "warning",
            $"El vehículo {telemetryEvent.DeviceId:D} tiene batería baja: {telemetryEvent.BatteryPercent:F1}%",
            ResolveNullableThreshold(
                telemetryEvent.BatteryPercent,
                options.LowBatteryPercent,
                breachedWhenBelow: true)));

        return observations;
    }

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
                telemetryEvent.DeviceId,
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

    public static AlertConditionTransition Transition(
        Guid deviceId,
        AlertConditionObservation observation,
        FleetAlertConditionState? current,
        DateTimeOffset now,
        TimeSpan cooldown)
    {
        return observation.Status switch
        {
            AlertConditionObservationStatus.NotObserved => AlertConditionTransition.NoChange,
            AlertConditionObservationStatus.Recovered => TransitionRecovered(current, now),
            AlertConditionObservationStatus.Breached => TransitionBreached(
                deviceId,
                observation,
                current,
                now,
                cooldown),
            _ => AlertConditionTransition.NoChange
        };
    }

    private static AlertConditionTransition TransitionRecovered(
        FleetAlertConditionState? current,
        DateTimeOffset now)
    {
        if (current is null || !current.IsActive)
            return AlertConditionTransition.NoChange;

        current.MarkInactive(now);
        return AlertConditionTransition.StateOnly(current);
    }

    private static AlertConditionTransition TransitionBreached(
        Guid deviceId,
        AlertConditionObservation observation,
        FleetAlertConditionState? current,
        DateTimeOffset now,
        TimeSpan cooldown)
    {
        var isActive = current?.IsActive == true;
        var cooldownElapsed = current?.LastAlertAt is null
            || now - current.LastAlertAt.Value >= cooldown;

        if (!isActive)
        {
            if (current is null)
            {
                var created = FleetAlertConditionState.Create(
                    deviceId,
                    observation.AlertType,
                    isActive: true,
                    lastConditionAt: now,
                    lastAlertAt: now,
                    updatedAt: now);
                return AlertConditionTransition.Emit(
                    CreateAlert(deviceId, observation, now),
                    created);
            }

            if (cooldownElapsed)
            {
                current.MarkActive(now, alertAt: now, updatedAt: now);
                return AlertConditionTransition.Emit(
                    CreateAlert(deviceId, observation, now),
                    current);
            }

            current.MarkActive(now, alertAt: null, updatedAt: now);
            return AlertConditionTransition.StateOnly(current);
        }

        current!.RefreshCondition(now, now);
        if (!cooldownElapsed)
            return AlertConditionTransition.StateOnly(current);

        current.MarkActive(now, alertAt: now, updatedAt: now);
        return AlertConditionTransition.Emit(
            CreateAlert(deviceId, observation, now),
            current);
    }

    private static AlertConditionObservationStatus ResolveNullableThreshold(
        double? value,
        double threshold,
        bool breachedWhenBelow)
    {
        if (value is null)
            return AlertConditionObservationStatus.NotObserved;

        var breached = breachedWhenBelow ? value.Value < threshold : value.Value > threshold;
        return breached
            ? AlertConditionObservationStatus.Breached
            : AlertConditionObservationStatus.Recovered;
    }

    private static FleetAlert CreateAlert(
        Guid deviceId,
        AlertConditionObservation observation,
        DateTimeOffset now) =>
        FleetAlert.Create(
            Guid.NewGuid(),
            deviceId,
            observation.AlertType,
            observation.Severity,
            observation.Message,
            now);
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
