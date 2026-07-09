using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;

// Pruebas del evaluador de alertas por telemetría.
namespace FleetTelemetry.Application.Tests;

public class TelemetryAlertEvaluatorTests
{
    [Fact]
    public void Evaluate_generates_overspeed_alert()
    {
        var evt = BaseEvent();
        evt.SpeedKmh = 130;
        var alerts = TelemetryAlertEvaluator.Evaluate(evt);
        Assert.Contains(alerts, a => a.AlertType == "overspeed" && a.Severity == "critical");
    }

    [Fact]
    public void Evaluate_generates_low_fuel_alert()
    {
        var evt = BaseEvent();
        evt.FuelLevelPercent = 10;
        var alerts = TelemetryAlertEvaluator.Evaluate(evt);
        Assert.Contains(alerts, a => a.AlertType == "low_fuel");
    }

    [Fact]
    public void Evaluate_returns_empty_when_within_limits()
    {
        var alerts = TelemetryAlertEvaluator.Evaluate(BaseEvent());
        Assert.Empty(alerts);
    }

    private static TelemetryEvent BaseEvent() => new()
    {
        EventId = Guid.NewGuid(),
        VehicleId = "VH-001",
        Timestamp = DateTimeOffset.UtcNow,
        Latitude = 4.65,
        Longitude = -74.08,
        SpeedKmh = 60,
        FuelLevelPercent = 50,
        BatteryPercent = 80,
    };
}
