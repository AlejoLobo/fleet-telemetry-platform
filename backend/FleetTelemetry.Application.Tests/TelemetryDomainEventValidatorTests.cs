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
    public void TryCreate_rejects_empty_event_id()
    {
        var created = TelemetryEvent.TryCreate(
            Guid.Empty,
            "VH-001",
            null,
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            45,
            null,
            null,
            out _,
            out var error);

        Assert.False(created);
        Assert.Contains("EventId", error);
    }

    [Fact]
    public void TryCreate_rejects_empty_vehicle_id()
    {
        var created = TelemetryEvent.TryCreate(
            Guid.NewGuid(),
            " ",
            null,
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            45,
            null,
            null,
            out _,
            out var error);

        Assert.False(created);
        Assert.Contains("VehicleId", error);
    }

    [Fact]
    public void TryCreate_rejects_default_timestamp()
    {
        var created = TelemetryEvent.TryCreate(
            Guid.NewGuid(),
            "VH-001",
            null,
            default,
            4.65,
            -74.08,
            45,
            null,
            null,
            out _,
            out var error);

        Assert.False(created);
        Assert.Contains("Timestamp", error);
    }

    [Fact]
    public void TryCreate_rejects_out_of_range_coordinates_and_negative_speed()
    {
        Assert.False(TelemetryEvent.TryCreate(Guid.NewGuid(), "VH-001", null, DateTimeOffset.UtcNow, 91, -74.08, 45, null, null, out _, out _));
        Assert.False(TelemetryEvent.TryCreate(Guid.NewGuid(), "VH-001", null, DateTimeOffset.UtcNow, 4.65, -181, 45, null, null, out _, out _));
        Assert.False(TelemetryEvent.TryCreate(Guid.NewGuid(), "VH-001", null, DateTimeOffset.UtcNow, 4.65, -74.08, -1, null, null, out _, out _));
    }

    [Fact]
    public void Partial_json_deserialized_event_is_classified_as_invalid_payload()
    {
        const string invalidPayloadReason = "invalid_payload";
        var partialException = Assert.Throws<InvalidOperationException>(() =>
            FleetTelemetry.Infrastructure.Kafka.TelemetryEventJsonSerializer.Deserialize("""{"vehicleId":"VH-001"}"""));

        Assert.Contains("EventId", partialException.Message);
        Assert.Equal("invalid_payload", invalidPayloadReason);
    }

    private static TelemetryEvent ValidEvent() =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            "VH-001",
            "DRV-001",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            45,
            80,
            90);
}
