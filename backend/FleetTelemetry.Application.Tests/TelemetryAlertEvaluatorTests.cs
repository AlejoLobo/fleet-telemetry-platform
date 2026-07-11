using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Tests;

public class TelemetryAlertEvaluatorTests
{
    [Fact]
    public void Evaluate_generates_overspeed_alert()
    {
        var evt = BaseEvent(speedKmh: 130);
        var alerts = TelemetryAlertEvaluator.Evaluate(evt);
        Assert.Contains(alerts, a => a.AlertType == "overspeed" && a.Severity == "critical");
    }

    [Fact]
    public void Evaluate_generates_low_fuel_alert()
    {
        var evt = BaseEvent(fuelLevelPercent: 10);
        var alerts = TelemetryAlertEvaluator.Evaluate(evt);
        Assert.Contains(alerts, a => a.AlertType == "low_fuel");
    }

    [Fact]
    public void Evaluate_returns_empty_when_within_limits()
    {
        var alerts = TelemetryAlertEvaluator.Evaluate(BaseEvent());
        Assert.Empty(alerts);
    }

    private static TelemetryEvent BaseEvent(
        double speedKmh = 60,
        double? fuelLevelPercent = 50,
        double? batteryPercent = 80) =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            "VH-001",
            null,
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            speedKmh,
            fuelLevelPercent,
            batteryPercent);
}
