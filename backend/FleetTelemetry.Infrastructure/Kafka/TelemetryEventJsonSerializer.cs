using System.Text.Json;
using System.Text.Json.Serialization;
using FleetTelemetry.Application.Contracts;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Infrastructure.Kafka;

// Serialización JSON con soporte de envelope versionado y payload legacy.
public static class TelemetryEventJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(TelemetryEvent telemetryEvent, bool useEnvelope = false)
    {
        if (!useEnvelope)
            return JsonSerializer.Serialize(ToLegacyDto(telemetryEvent), Options);

        var envelope = TelemetryEventEnvelope.Wrap(telemetryEvent);
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static TelemetryEvent Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unable to deserialize telemetry event.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("schemaVersion", out _) || root.TryGetProperty("payload", out _))
        {
            var envelope = JsonSerializer.Deserialize<TelemetryEventEnvelope>(json, Options)
                ?? throw new InvalidOperationException("Unable to deserialize telemetry envelope.");

            return envelope.Payload;
        }

        var legacy = JsonSerializer.Deserialize<LegacyTelemetryEventDto>(json, Options)
            ?? throw new InvalidOperationException("Unable to deserialize telemetry event.");

        return TelemetryEvent.TryCreate(
            legacy.EventId,
            legacy.VehicleId,
            legacy.DriverId,
            legacy.Timestamp,
            legacy.Latitude,
            legacy.Longitude,
            legacy.SpeedKmh,
            legacy.FuelLevelPercent,
            legacy.BatteryPercent,
            out var telemetryEvent,
            out var error,
            locationSource: legacy.LocationSource)
            ? telemetryEvent!
            : throw new InvalidOperationException(error ?? "Unable to deserialize telemetry event.");
    }

    private static LegacyTelemetryEventDto ToLegacyDto(TelemetryEvent telemetryEvent) => new()
    {
        EventId = telemetryEvent.EventId,
        VehicleId = telemetryEvent.VehicleId,
        DriverId = telemetryEvent.DriverId,
        Timestamp = telemetryEvent.Timestamp,
        Latitude = telemetryEvent.Latitude,
        Longitude = telemetryEvent.Longitude,
        SpeedKmh = telemetryEvent.SpeedKmh,
        FuelLevelPercent = telemetryEvent.FuelLevelPercent,
        BatteryPercent = telemetryEvent.BatteryPercent,
        LocationSource = telemetryEvent.LocationSource
    };

    private sealed class LegacyTelemetryEventDto
    {
        public Guid EventId { get; set; }
        public string VehicleId { get; set; } = string.Empty;
        public string? DriverId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double SpeedKmh { get; set; }
        public double? FuelLevelPercent { get; set; }
        public double? BatteryPercent { get; set; }
        public string? LocationSource { get; set; }
    }
}
