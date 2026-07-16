using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Contracts;

public sealed class TelemetryEventPayloadV1
{
    public Guid EventId { get; init; }
    public Guid DeviceId { get; init; }
    public string? DriverId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double SpeedKmh { get; init; }
    public double? FuelLevelPercent { get; init; }
    public double? BatteryPercent { get; init; }
    public string? LocationSource { get; init; }

    public static TelemetryEventPayloadV1 FromDomain(TelemetryEvent telemetryEvent) => new()
    {
        EventId = telemetryEvent.EventId,
        DeviceId = telemetryEvent.DeviceId,
        DriverId = telemetryEvent.DriverId,
        Timestamp = telemetryEvent.Timestamp,
        Latitude = telemetryEvent.Latitude,
        Longitude = telemetryEvent.Longitude,
        SpeedKmh = telemetryEvent.SpeedKmh,
        FuelLevelPercent = telemetryEvent.FuelLevelPercent,
        BatteryPercent = telemetryEvent.BatteryPercent,
        LocationSource = telemetryEvent.LocationSource
    };
}
