using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Tests;

public class TelemetryAlertEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly AlertingOptions Options = new()
    {
        CooldownSeconds = 300,
        OverspeedThresholdKmh = 120,
        LowFuelPercent = 15,
        LowBatteryPercent = 20
    };

    [Fact]
    public void Valor_normal_no_genera_alerta()
    {
        var result = Evaluate(BaseEvent(), now: T0);
        Assert.Empty(result.EmittedAlerts);
        Assert.Empty(result.StatesToUpsert);
    }

    [Fact]
    public void Primer_overspeed_genera_una_alerta()
    {
        var result = Evaluate(BaseEvent(speedKmh: 130), now: T0);
        Assert.Single(result.EmittedAlerts);
        Assert.Equal("overspeed", result.EmittedAlerts[0].AlertType);
        Assert.Equal("critical", result.EmittedAlerts[0].Severity);
        Assert.Equal(T0, result.EmittedAlerts[0].CreatedAt);
        Assert.Single(result.StatesToUpsert);
        Assert.True(result.StatesToUpsert[0].IsActive);
        Assert.Equal(T0, result.StatesToUpsert[0].LastAlertAt);
    }

    [Fact]
    public void SpeedKmh_igual_a_120_no_genera_overspeed()
    {
        var result = Evaluate(BaseEvent(speedKmh: 120), now: T0);
        Assert.DoesNotContain(result.EmittedAlerts, a => a.AlertType == "overspeed");
    }

    [Fact]
    public void Fuel_igual_a_15_no_genera_low_fuel()
    {
        var result = Evaluate(BaseEvent(fuelLevelPercent: 15), now: T0);
        Assert.DoesNotContain(result.EmittedAlerts, a => a.AlertType == "low_fuel");
    }

    [Fact]
    public void Battery_igual_a_20_no_genera_low_battery()
    {
        var result = Evaluate(BaseEvent(batteryPercent: 20), now: T0);
        Assert.DoesNotContain(result.EmittedAlerts, a => a.AlertType == "low_battery");
    }

    [Fact]
    public void Distintos_tipos_se_evaluan_independentemente()
    {
        var result = Evaluate(
            BaseEvent(speedKmh: 130, fuelLevelPercent: 10, batteryPercent: 10),
            now: T0);

        Assert.Equal(3, result.EmittedAlerts.Count);
        Assert.Contains(result.EmittedAlerts, a => a.AlertType == "overspeed");
        Assert.Contains(result.EmittedAlerts, a => a.AlertType == "low_fuel");
        Assert.Contains(result.EmittedAlerts, a => a.AlertType == "low_battery");
        Assert.Equal(3, result.StatesToUpsert.Count);
    }

    [Fact]
    public void Tiempo_controlado_mediante_TimeProvider_equivalente()
    {
        var time = new MutableTimeProvider(T0);
        var first = Evaluate(BaseEvent(speedKmh: 130), now: time.GetUtcNow());
        Assert.Single(first.EmittedAlerts);

        time.SetUtcNow(T0.AddMinutes(1));
        var states = first.StatesToUpsert.ToDictionary(s => s.AlertType);
        var suppressed = Evaluate(BaseEvent(speedKmh: 135), states, time.GetUtcNow());
        Assert.Empty(suppressed.EmittedAlerts);
        Assert.True(suppressed.StatesToUpsert.Single().IsActive);

        time.SetUtcNow(T0.AddSeconds(Options.CooldownSeconds));
        var reminder = Evaluate(BaseEvent(speedKmh: 140), states, time.GetUtcNow());
        Assert.Single(reminder.EmittedAlerts);
        Assert.Equal(time.GetUtcNow(), reminder.EmittedAlerts[0].CreatedAt);
    }

    [Fact]
    public void Condicion_activa_dentro_del_cooldown_no_emite()
    {
        var active = FleetAlertConditionState.Create(
            "VH-001", "overspeed", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(speedKmh: 130),
            new Dictionary<string, FleetAlertConditionState> { ["overspeed"] = active },
            T0.AddSeconds(10));

        Assert.Empty(result.EmittedAlerts);
        Assert.Equal(T0.AddSeconds(10), result.StatesToUpsert.Single().LastConditionAt);
        Assert.Equal(T0, result.StatesToUpsert.Single().LastAlertAt);
    }

    [Fact]
    public void Recuperacion_marca_inactiva_sin_usar_acknowledged()
    {
        var active = FleetAlertConditionState.Create(
            "VH-001", "overspeed", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(speedKmh: 80),
            new Dictionary<string, FleetAlertConditionState> { ["overspeed"] = active },
            T0.AddMinutes(1));

        Assert.Empty(result.EmittedAlerts);
        Assert.False(result.StatesToUpsert.Single().IsActive);
        Assert.Equal(T0, result.StatesToUpsert.Single().LastAlertAt);
    }

    [Fact]
    public void Nueva_incidencia_tras_recuperacion_respeta_cooldown()
    {
        var recovered = FleetAlertConditionState.Create(
            "VH-001", "overspeed", false, T0, T0, T0.AddMinutes(1));

        var within = Evaluate(
            BaseEvent(speedKmh: 130),
            new Dictionary<string, FleetAlertConditionState> { ["overspeed"] = recovered },
            T0.AddMinutes(2));
        Assert.Empty(within.EmittedAlerts);
        Assert.True(within.StatesToUpsert.Single().IsActive);

        recovered.MarkInactive(T0.AddMinutes(3));
        var after = Evaluate(
            BaseEvent(speedKmh: 130),
            new Dictionary<string, FleetAlertConditionState> { ["overspeed"] = recovered },
            T0.AddSeconds(Options.CooldownSeconds));
        Assert.Single(after.EmittedAlerts);
    }

    private static AlertEvaluationResult Evaluate(
        TelemetryEvent telemetryEvent,
        DateTimeOffset now) =>
        TelemetryAlertEvaluator.Evaluate(telemetryEvent, null, now, Options);

    private static AlertEvaluationResult Evaluate(
        TelemetryEvent telemetryEvent,
        IReadOnlyDictionary<string, FleetAlertConditionState> states,
        DateTimeOffset now) =>
        TelemetryAlertEvaluator.Evaluate(telemetryEvent, states, now, Options);

    private static TelemetryEvent BaseEvent(
        double speedKmh = 60,
        double? fuelLevelPercent = 50,
        double? batteryPercent = 80) =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            "VH-001",
            null,
            T0,
            4.65,
            -74.08,
            speedKmh,
            fuelLevelPercent,
            batteryPercent);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
