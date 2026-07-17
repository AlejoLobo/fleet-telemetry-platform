using FleetTelemetry.Application.Contracts;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Kafka;
using Xunit;

namespace FleetTelemetry.Application.Tests;

public sealed class TelemetryEnvelopeIntegrityTests
{
    private static readonly TelemetryEvent Sample = TelemetryEvent.Create(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "DRV-001",
        new DateTimeOffset(2026, 7, 12, 8, 30, 0, TimeSpan.Zero),
        4.6533,
        -74.0836,
        45.5,
        80,
        95);

    private static TelemetryEvent DeserializeEnvelope(string json) =>
        TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: true);

    private static TelemetryEvent DeserializeLegacy(string json) =>
        TelemetryEventJsonSerializer.Deserialize(json, useEventEnvelope: false);

    [Fact]
    public void Envelope_sin_eventId_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Envelope_con_eventId_Guid_Empty_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "00000000-0000-0000-0000-000000000000",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Envelope_sin_occurredAt_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Envelope_con_occurredAt_default_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "0001-01-01T00:00:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Envelope_con_EventId_diferente_al_payload_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "22222222-2222-2222-2222-222222222222",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Envelope_con_occurredAt_diferente_al_payload_timestamp_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T09:00:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Envelope_con_mismo_instante_y_diferente_offset_es_aceptado()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T03:30:00-05:00",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var result = DeserializeEnvelope(json);
        Assert.Equal(Sample.EventId, result.EventId);
        Assert.Equal(Sample.Timestamp, result.Timestamp);
    }

    [Fact]
    public void SchemaVersion_2_con_payload_incompatible_devuelve_unsupported_schema_version()
    {
        var json = """
            {
              "schemaVersion": 2,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("unsupported_schema_version", ex.ErrorCode);
    }

    [Fact]
    public void SchemaVersion_como_string_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": "1",
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void SchemaVersion_ausente_devuelve_invalid_envelope()
    {
        var json = """
            {
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void EventType_ausente_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void EventType_vacio_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Payload_ausente_devuelve_invalid_envelope()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z"
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Payload_null_devuelve_null_payload()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "payload": null
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeEnvelope(json));
        Assert.Equal("null_payload", ex.ErrorCode);
    }

    [Fact]
    public void Legacy_valido_con_campo_reservado_schemaVersion_devuelve_invalid_envelope()
    {
        var json = """
            {
              "eventId": "11111111-1111-1111-1111-111111111111",
              "deviceId": "11111111-1111-1111-1111-111111111111",
              "driverId": "DRV-001",
              "timestamp": "2026-07-12T08:30:00Z",
              "latitude": 4.6533,
              "longitude": -74.0836,
              "speedKmh": 45.5,
              "fuelLevelPercent": 80,
              "batteryPercent": 95,
              "schemaVersion": 1
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeLegacy(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Legacy_valido_con_campo_reservado_payload_devuelve_invalid_envelope()
    {
        var json = """
            {
              "eventId": "11111111-1111-1111-1111-111111111111",
              "deviceId": "11111111-1111-1111-1111-111111111111",
              "driverId": "DRV-001",
              "timestamp": "2026-07-12T08:30:00Z",
              "latitude": 4.6533,
              "longitude": -74.0836,
              "speedKmh": 45.5,
              "fuelLevelPercent": 80,
              "batteryPercent": 95,
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111"
              }
            }
            """;

        var ex = Assert.Throws<TelemetryKafkaContractException>(() => DeserializeLegacy(json));
        Assert.Equal("invalid_envelope", ex.ErrorCode);
    }

    [Fact]
    public void Round_trip_V1_continua_funcionando()
    {
        var json = TelemetryEventJsonSerializer.Serialize(Sample, useEnvelope: true);
        var roundTrip = DeserializeEnvelope(json);
        Assert.Equal(Sample.EventId, roundTrip.EventId);
        Assert.Equal(Sample.DeviceId, roundTrip.DeviceId);
        Assert.Equal(Sample.Timestamp, roundTrip.Timestamp);
    }

    [Fact]
    public void Campos_adicionales_no_reservados_continuan_siendo_tolerados()
    {
        var json = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "11111111-1111-1111-1111-111111111111",
              "occurredAt": "2026-07-12T08:30:00Z",
              "correlationId": "corr-123",
              "payload": {
                "eventId": "11111111-1111-1111-1111-111111111111",
                "deviceId": "11111111-1111-1111-1111-111111111111",
                "driverId": "DRV-001",
                "timestamp": "2026-07-12T08:30:00Z",
                "latitude": 4.6533,
                "longitude": -74.0836,
                "speedKmh": 45.5,
                "fuelLevelPercent": 80,
                "batteryPercent": 95,
                "extraField": "ignored"
              }
            }
            """;

        var result = DeserializeEnvelope(json);
        Assert.Equal(Sample.EventId, result.EventId);
    }
}
