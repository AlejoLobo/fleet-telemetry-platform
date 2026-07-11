using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Validation;

public class TelemetryEventValidator
{
    private readonly TelemetryIngestOptions _options;
    private readonly TimeProvider _timeProvider;

    public TelemetryEventValidator(IOptions<TelemetryIngestOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public void Validate(TelemetryEventRequest request)
    {
        if (request.EventId == Guid.Empty)
            throw new ArgumentException("EventId is required.");

        if (string.IsNullOrWhiteSpace(request.VehicleId))
            throw new ArgumentException("VehicleId is required.");

        if (request.VehicleId.Trim().Length > _options.MaxVehicleIdLength)
            throw new ArgumentException($"VehicleId must be <= {_options.MaxVehicleIdLength} characters.");

        if (request.DriverId is not null && request.DriverId.Trim().Length > _options.MaxDriverIdLength)
            throw new ArgumentException($"DriverId must be <= {_options.MaxDriverIdLength} characters.");

        if (request.Timestamp == default)
            throw new ArgumentException("Timestamp is required.");

        var now = _timeProvider.GetUtcNow();
        if (request.Timestamp > now.AddMinutes(_options.MaxFutureSkewMinutes))
            throw new ArgumentException("Timestamp cannot be too far in the future.");

        if (request.Timestamp < now.AddDays(-_options.MaxPastSkewDays))
            throw new ArgumentException("Timestamp is too old.");

        if (request.Latitude is < -90 or > 90)
            throw new ArgumentException("Latitude must be between -90 and 90.");

        if (request.Longitude is < -180 or > 180)
            throw new ArgumentException("Longitude must be between -180 and 180.");

        if (request.SpeedKmh < 0)
            throw new ArgumentException("SpeedKmh must be >= 0.");

        if (request.SpeedKmh > _options.MaxSpeedKmh)
            throw new ArgumentException($"SpeedKmh must be <= {_options.MaxSpeedKmh}.");

        if (request.FuelLevelPercent is < 0 or > 100)
            throw new ArgumentException("FuelLevelPercent must be between 0 and 100.");

        if (request.BatteryPercent is < 0 or > 100)
            throw new ArgumentException("BatteryPercent must be between 0 and 100.");

        var source = NormalizeLocationSource(request.LocationSource);
        if (source is not ("gps" or "simulated"))
            throw new ArgumentException("LocationSource must be gps or simulated.");
    }

    public TelemetryEvent MapToDomain(TelemetryEventRequest request) =>
        TelemetryEvent.Create(
            request.EventId,
            request.VehicleId.Trim(),
            request.DriverId?.Trim(),
            request.Timestamp,
            request.Latitude,
            request.Longitude,
            request.SpeedKmh,
            request.FuelLevelPercent,
            request.BatteryPercent,
            NormalizeLocationSource(request.LocationSource));

    public static string NormalizeLocationSource(string? source) =>
        string.IsNullOrWhiteSpace(source) ? "gps" : source.Trim().ToLowerInvariant();
}
