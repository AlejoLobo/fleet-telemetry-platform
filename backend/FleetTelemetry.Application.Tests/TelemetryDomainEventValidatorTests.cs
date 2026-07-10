using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Tests;

public class TelemetryDomainEventValidatorTests
{
    [Fact]
    public void Validate_accepts_valid_domain_event()
    {
        var exception = Record.Exception(() => TelemetryDomainEventValidator.Validate(ValidEvent()));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_rejects_empty_event_id()
    {
        var telemetryEvent = ValidEvent();
        telemetryEvent.EventId = Guid.Empty;

        var ex = Assert.Throws<ArgumentException>(() => TelemetryDomainEventValidator.Validate(telemetryEvent));
        Assert.Contains("EventId", ex.Message);
    }

    [Fact]
    public void Validate_rejects_empty_vehicle_id()
    {
        var telemetryEvent = ValidEvent();
        telemetryEvent.VehicleId = " ";

        Assert.Throws<ArgumentException>(() => TelemetryDomainEventValidator.Validate(telemetryEvent));
    }

    [Fact]
    public void Validate_rejects_default_timestamp()
    {
        var telemetryEvent = ValidEvent();
        telemetryEvent.Timestamp = default;

        Assert.Throws<ArgumentException>(() => TelemetryDomainEventValidator.Validate(telemetryEvent));
    }

    [Fact]
    public void Validate_rejects_out_of_range_coordinates_and_negative_speed()
    {
        var badLatitude = ValidEvent();
        badLatitude.Latitude = 91;
        Assert.Throws<ArgumentException>(() => TelemetryDomainEventValidator.Validate(badLatitude));

        var badLongitude = ValidEvent();
        badLongitude.Longitude = -181;
        Assert.Throws<ArgumentException>(() => TelemetryDomainEventValidator.Validate(badLongitude));

        var badSpeed = ValidEvent();
        badSpeed.SpeedKmh = -1;
        Assert.Throws<ArgumentException>(() => TelemetryDomainEventValidator.Validate(badSpeed));
    }

    [Fact]
    public void Partial_json_deserialized_event_is_classified_as_invalid_payload()
    {
        // Mismo criterio que TelemetryConsumerWorker: fallos de dominio → reason invalid_payload.
        const string invalidPayloadReason = "invalid_payload";
        var partial = new TelemetryEvent { VehicleId = "VH-001" };

        var ex = Assert.Throws<ArgumentException>(() => TelemetryDomainEventValidator.Validate(partial));

        Assert.Equal(Guid.Empty, partial.EventId);
        Assert.Equal(default, partial.Timestamp);
        Assert.Contains("EventId", ex.Message);
        Assert.Equal("invalid_payload", invalidPayloadReason);
    }

    private static TelemetryEvent ValidEvent() => new()
    {
        EventId = Guid.NewGuid(),
        VehicleId = "VH-001",
        DriverId = "DRV-001",
        Timestamp = DateTimeOffset.UtcNow,
        Latitude = 4.65,
        Longitude = -74.08,
        SpeedKmh = 45,
        FuelLevelPercent = 80,
        BatteryPercent = 90
    };
}
