using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Validation;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

public class TelemetryEventValidatorTests
{
    private readonly TelemetryEventValidator _validator = new(
        Options.Create(new TelemetryIngestOptions()),
        TimeProvider.System);

    [Fact]
    public void Validate_accepts_valid_request()
    {
        var exception = Record.Exception(() => _validator.Validate(ValidRequest()));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_rejects_invalid_location_source()
    {
        Assert.Throws<ArgumentException>(() => _validator.Validate(ValidRequest() with { LocationSource = "fake" }));
    }

    private static TelemetryEventRequest ValidRequest() => new(
        Guid.NewGuid(), Guid.Parse("11111111-1111-1111-1111-111111111111"), "DRV-001", DateTimeOffset.UtcNow, 4.65, -74.08, 45, 80, 90, "gps");
}
