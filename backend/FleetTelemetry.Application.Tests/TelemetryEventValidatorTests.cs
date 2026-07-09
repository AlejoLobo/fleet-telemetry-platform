using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Validation;

// Pruebas de validación de eventos de telemetría.
namespace FleetTelemetry.Application.Tests;

public class TelemetryEventValidatorTests
{
    [Fact]
    public void Validate_accepts_valid_request()
    {
        var request = ValidRequest();
        var exception = Record.Exception(() => TelemetryEventValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_rejects_empty_vehicle_id()
    {
        var request = ValidRequest() with { VehicleId = " " };
        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(request));
    }

    [Fact]
    public void Validate_rejects_negative_speed()
    {
        var request = ValidRequest() with { SpeedKmh = -1 };
        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(request));
    }

    private static TelemetryEventRequest ValidRequest() => new(
        EventId: Guid.NewGuid(),
        VehicleId: "VH-001",
        DriverId: "DRV-001",
        Timestamp: DateTimeOffset.UtcNow,
        Latitude: 4.65,
        Longitude: -74.08,
        SpeedKmh: 45,
        FuelLevelPercent: 80,
        BatteryPercent: 90);
}
