using System.Text.Json;
using FleetTelemetry.Application.Contracts;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Kafka;

namespace FleetTelemetry.Application.Tests;

public class TelemetryEventJsonSerializerTests
{
    private static TelemetryEvent CreateSampleEvent()
    {
        var eventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var timestamp = new DateTimeOffset(2026, 7, 12, 8, 30, 0, TimeSpan.Zero);
        return TelemetryEvent.Create(
            eventId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "DRV-99",
            timestamp,
            4.6533,
            -74.0836,
            88.5,
            42.0,
            91.0,
            "gps");
    }

    [Fact]
    public void Round_trip_v1_preserves_all_fields()
    {
        var original = CreateSampleEvent();
        var json = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: true);
        var restored = TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true);

        Assert.Equal(original.EventId, restored.EventId);
        Assert.Equal(original.DeviceId, restored.DeviceId);
        Assert.Equal(original.DriverId, restored.DriverId);
        Assert.Equal(original.Timestamp, restored.Timestamp);
        Assert.Equal(original.Latitude, restored.Latitude);
        Assert.Equal(original.Longitude, restored.Longitude);
        Assert.Equal(original.SpeedKmh, restored.SpeedKmh);
        Assert.Equal(original.FuelLevelPercent, restored.FuelLevelPercent);
        Assert.Equal(original.BatteryPercent, restored.BatteryPercent);
        Assert.Equal(original.LocationSource, restored.LocationSource);
    }

    [Fact]
    public void Serialize_v1_uses_camelCase()
    {
        var json = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: true);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("schemaVersion", out _));
        Assert.True(root.TryGetProperty("eventType", out _));
        Assert.True(root.TryGetProperty("eventId", out _));
        Assert.True(root.TryGetProperty("occurredAt", out _));
        Assert.True(root.TryGetProperty("payload", out var payload));
        Assert.True(payload.TryGetProperty("deviceId", out _));
        Assert.True(payload.TryGetProperty("speedKmh", out _));
        Assert.False(root.TryGetProperty("SchemaVersion", out _));
    }

    [Fact]
    public void Serialize_v1_writes_utc_timestamp()
    {
        var json = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: true);
        using var doc = JsonDocument.Parse(json);
        var occurredAt = doc.RootElement.GetProperty("occurredAt").GetString();
        Assert.Matches("2026-07-12T08:30:00(\\+00:00|Z)", occurredAt);
    }

    [Fact]
    public void Deserialize_v1_accepts_schema_version_1()
    {
        var json = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: true);
        var restored = TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true);
        Assert.NotEqual(Guid.Empty, restored.EventId);
    }

    [Fact]
    public void Deserialize_v1_rejects_unsupported_schema_version()
    {
        var json = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: true);
        var mutated = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":99", StringComparison.Ordinal);

        var ex = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize(mutated, useEventEnvelope: true));

        Assert.Equal("unsupported_schema_version", ex.ErrorCode);
    }

    [Fact]
    public void Deserialize_v1_rejects_envelope_without_payload()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "occurredAt": "2026-07-12T08:30:00Z"
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true));

        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Deserialize_v1_rejects_null_payload()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": null
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true));

        Assert.Equal("null_payload", ex.ErrorCode);
    }

    [Fact]
    public void Deserialize_v1_rejects_unknown_event_type()
    {
        var json = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: true)
            .Replace("fleet.telemetry.received", "unknown.type", StringComparison.Ordinal);

        var ex = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true));

        Assert.Equal("unknown_event_type", ex.ErrorCode);
    }

    [Fact]
    public void Deserialize_rejects_invalid_json()
    {
        var ex = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize("{ not-json", useEventEnvelope: false));

        Assert.Equal("invalid_json", ex.ErrorCode);
    }

    [Fact]
    public void Deserialize_v1_rejects_domain_invalid_payload()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                "deviceId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 95.0,
                "longitude": -74.0,
                "speedKmh": 10
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true));

        Assert.Equal("invalid_domain", ex.ErrorCode);
    }

    [Fact]
    public void Deserialize_legacy_valid_when_use_event_envelope_false()
    {
        var original = CreateSampleEvent();
        var json = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: false);
        var restored = TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: false);
        Assert.Equal(original.EventId, restored.EventId);
    }

    [Fact]
    public void Deserialize_v1_valid_when_use_event_envelope_true()
    {
        var original = CreateSampleEvent();
        var json = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: true);
        var restored = TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true);
        Assert.Equal(original.DeviceId, restored.DeviceId);
    }

    [Fact]
    public void Deserialize_envelope_mode_does_not_fall_back_to_legacy_for_flat_json()
    {
        var legacyJson = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: false);

        var ex = Assert.Throws<TelemetryKafkaContractException>(() =>
            TelemetryEventJsonSerializer.Deserialize(legacyJson, useEventEnvelope: true));

        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Deserialize_v1_ignores_safe_extra_fields()
    {
        var json = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: true)
            .Replace(
                "\"payload\":",
                "\"futureField\":\"ignored\",\"payload\":",
                StringComparison.Ordinal);

        var restored = TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), restored.DeviceId);
    }

    [Fact]
    public void Deserialize_legacy_duplicate_event_id_is_handled_by_downstream_idempotency()
    {
        var original = CreateSampleEvent();
        var json = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: false);
        var first = TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: false);
        var second = TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: false);
        Assert.Equal(first.EventId, second.EventId);
    }
}
