using System.Text.Json;
using System.Text.Json.Serialization;
using FleetTelemetry.Application.Contracts;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Infrastructure.Kafka;

// Serialización JSON con contrato envelope V1 (DTO) y payload legacy plano.
public static class TelemetryEventJsonSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize(TelemetryEvent telemetryEvent, bool useEnvelope = false)
    {
        if (!useEnvelope)
            return JsonSerializer.Serialize(ToLegacyDto(telemetryEvent), Options);

        var envelope = TelemetryEventEnvelopeV1.FromDomain(telemetryEvent);
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static TelemetryEvent Deserialize(string json, bool useEventEnvelope = false)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            throw new TelemetryKafkaContractException("null_payload", "Payload is null, empty or whitespace.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new TelemetryKafkaContractException("invalid_json", $"Invalid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (useEventEnvelope)
                return DeserializeEnvelopeV1(root, json);

            TelemetryEnvelopeContractInspector.EnsureLegacyHasNoReservedFields(root);
            return DeserializeLegacy(json);
        }
    }

    private static TelemetryEvent DeserializeEnvelopeV1(JsonElement root, string json)
    {
        TelemetryEnvelopeContractInspector.EnsureEnvelopeObject(root);
        TelemetryEnvelopeContractInspector.ReadAndValidateSchemaVersion(root);
        TelemetryEnvelopeContractInspector.EnsureEnvelopeRequiredMetadataPresent(root);

        TelemetryEventEnvelopeV1? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<TelemetryEventEnvelopeV1>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new TelemetryKafkaContractException("invalid_envelope", $"Invalid envelope JSON: {ex.Message}");
        }

        if (envelope is null)
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope deserialization returned null.");

        return TelemetryKafkaContractMapper.MapEnvelopeV1ToDomain(envelope);
    }

    private static TelemetryEvent DeserializeLegacy(string json)
    {
        LegacyTelemetryEventDto? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<LegacyTelemetryEventDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new TelemetryKafkaContractException("invalid_json", $"Invalid JSON: {ex.Message}");
        }

        if (legacy is null)
            throw new TelemetryKafkaContractException("null_payload", "Legacy payload is null.");

        if (!TelemetryEvent.TryCreate(
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
                locationSource: legacy.LocationSource))
        {
            throw new TelemetryKafkaContractException("invalid_domain", error ?? "Domain validation failed.");
        }

        return telemetryEvent!;
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
