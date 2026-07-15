using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Tests;

public class TelemetryDomainEventValidatorTests
{
    private static readonly Guid DeviceA = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
            DeviceA,
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
    public void TryCreate_rejects_empty_device_id()
    {
        var created = TelemetryEvent.TryCreate(
            Guid.NewGuid(),
            Guid.Empty,
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
        Assert.Contains("DeviceId", error);
    }

    [Fact]
    public void TryCreate_rejects_default_timestamp()
    {
        var created = TelemetryEvent.TryCreate(
            Guid.NewGuid(),
            DeviceA,
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
        Assert.False(TelemetryEvent.TryCreate(Guid.NewGuid(), DeviceA, null, DateTimeOffset.UtcNow, 91, -74.08, 45, null, null, out _, out _));
        Assert.False(TelemetryEvent.TryCreate(Guid.NewGuid(), DeviceA, null, DateTimeOffset.UtcNow, 4.65, -181, 45, null, null, out _, out _));
        Assert.False(TelemetryEvent.TryCreate(Guid.NewGuid(), DeviceA, null, DateTimeOffset.UtcNow, 4.65, -74.08, -1, null, null, out _, out _));
    }

    [Fact]
    public void Partial_json_deserialized_event_is_classified_as_invalid_payload()
    {
        const string invalidDomainReason = "invalid_domain";
        var partialException = Assert.Throws<TelemetryKafkaContractException>(() =>
            FleetTelemetry.Infrastructure.Kafka.TelemetryEventJsonSerializer.Deserialize(
                """{"deviceId":"11111111-1111-1111-1111-111111111111"}"""));

        Assert.Contains("EventId", partialException.Message);
        Assert.Equal("invalid_domain", partialException.ErrorCode);
        Assert.Equal("invalid_domain", invalidDomainReason);
    }

    private static TelemetryEvent ValidEvent() =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            DeviceA,
            "DRV-001",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            45,
            80,
            90);
}
