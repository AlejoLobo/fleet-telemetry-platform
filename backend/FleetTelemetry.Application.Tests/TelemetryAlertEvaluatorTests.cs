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

    [Fact]
    public void Low_fuel_activo_con_fuel_null_conserva_IsActive()
    {
        var active = FleetAlertConditionState.Create(
            "VH-001", "low_fuel", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(fuelLevelPercent: null),
            new Dictionary<string, FleetAlertConditionState> { ["low_fuel"] = active },
            T0.AddMinutes(1));

        Assert.Empty(result.EmittedAlerts);
        Assert.Empty(result.StatesToUpsert);
        Assert.True(active.IsActive);
    }

    [Fact]
    public void Low_battery_activo_con_battery_null_conserva_IsActive()
    {
        var active = FleetAlertConditionState.Create(
            "VH-001", "low_battery", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(batteryPercent: null),
            new Dictionary<string, FleetAlertConditionState> { ["low_battery"] = active },
            T0.AddMinutes(1));

        Assert.Empty(result.EmittedAlerts);
        Assert.Empty(result.StatesToUpsert);
        Assert.True(active.IsActive);
    }

    [Fact]
    public void Null_sin_estado_previo_no_crea_estado()
    {
        var result = Evaluate(
            BaseEvent(fuelLevelPercent: null, batteryPercent: null),
            now: T0);

        Assert.Empty(result.EmittedAlerts);
        Assert.DoesNotContain(result.StatesToUpsert, s => s.AlertType is "low_fuel" or "low_battery");
    }

    [Fact]
    public void Null_despues_del_cooldown_no_genera_recordatorio()
    {
        var activeFuel = FleetAlertConditionState.Create(
            "VH-001", "low_fuel", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(fuelLevelPercent: null),
            new Dictionary<string, FleetAlertConditionState> { ["low_fuel"] = activeFuel },
            T0.AddSeconds(Options.CooldownSeconds));

        Assert.Empty(result.EmittedAlerts);
        Assert.Empty(result.StatesToUpsert);
        Assert.True(activeFuel.IsActive);
        Assert.Equal(T0, activeFuel.LastAlertAt);
    }

    [Fact]
    public void Fuel_igual_15_recupera_low_fuel()
    {
        var active = FleetAlertConditionState.Create(
            "VH-001", "low_fuel", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(fuelLevelPercent: 15),
            new Dictionary<string, FleetAlertConditionState> { ["low_fuel"] = active },
            T0.AddMinutes(1));

        Assert.Empty(result.EmittedAlerts);
        Assert.False(result.StatesToUpsert.Single(s => s.AlertType == "low_fuel").IsActive);
    }

    [Fact]
    public void Battery_igual_20_recupera_low_battery()
    {
        var active = FleetAlertConditionState.Create(
            "VH-001", "low_battery", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(batteryPercent: 20),
            new Dictionary<string, FleetAlertConditionState> { ["low_battery"] = active },
            T0.AddMinutes(1));

        Assert.Empty(result.EmittedAlerts);
        Assert.False(result.StatesToUpsert.Single(s => s.AlertType == "low_battery").IsActive);
    }

    [Fact]
    public void Campo_NotObserved_no_altera_otros_tipos()
    {
        var fuelActive = FleetAlertConditionState.Create(
            "VH-001", "low_fuel", true, T0, T0, T0);
        var result = Evaluate(
            BaseEvent(speedKmh: 130, fuelLevelPercent: null, batteryPercent: 80),
            new Dictionary<string, FleetAlertConditionState> { ["low_fuel"] = fuelActive },
            T0.AddSeconds(10));

        Assert.Contains(result.EmittedAlerts, a => a.AlertType == "overspeed");
        Assert.DoesNotContain(result.EmittedAlerts, a => a.AlertType == "low_fuel");
        Assert.DoesNotContain(result.StatesToUpsert, s => s.AlertType == "low_fuel");
        Assert.True(fuelActive.IsActive);
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

public class AlertingOptionsValidatorTests
{
    private readonly AlertingOptionsValidator _validator = new();

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Rechaza_OverspeedThresholdKmh_no_finito(double value)
    {
        var result = _validator.Validate(null, new AlertingOptions { OverspeedThresholdKmh = value });
        Assert.True(result.Failed);
        Assert.Contains("OverspeedThresholdKmh", result.FailureMessage);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Rechaza_LowFuelPercent_no_finito(double value)
    {
        var result = _validator.Validate(null, new AlertingOptions { LowFuelPercent = value });
        Assert.True(result.Failed);
        Assert.Contains("LowFuelPercent", result.FailureMessage);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Rechaza_LowBatteryPercent_no_finito(double value)
    {
        var result = _validator.Validate(null, new AlertingOptions { LowBatteryPercent = value });
        Assert.True(result.Failed);
        Assert.Contains("LowBatteryPercent", result.FailureMessage);
    }

    [Fact]
    public void Acepta_valores_finitos_validos()
    {
        var result = _validator.Validate(null, new AlertingOptions());
        Assert.False(result.Failed);
    }
}
