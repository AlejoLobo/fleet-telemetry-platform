using System.Text.Json;
using FleetTelemetry.Application.Exceptions;

namespace FleetTelemetry.Application.Contracts;

// Reglas de integridad del contrato envelope V1 inspeccionadas sobre JSON crudo.
public static class TelemetryEnvelopeContractInspector
{
    public static readonly string[] ReservedEnvelopePropertyNames =
    [
        "schemaVersion",
        "eventType",
        "eventId",
        "occurredAt",
        "payload",
    ];

    // Campos que indican estructura envelope y no deben aparecer en JSON legacy plano.
    private static readonly string[] LegacyForbiddenEnvelopePropertyNames =
    [
        "schemaVersion",
        "eventType",
        "occurredAt",
        "payload",
    ];

    public static void EnsureLegacyHasNoReservedFields(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new TelemetryKafkaContractException("invalid_envelope", "Legacy payload must be a JSON object.");

        foreach (var propertyName in LegacyForbiddenEnvelopePropertyNames)
        {
            if (root.TryGetProperty(propertyName, out _))
            {
                throw new TelemetryKafkaContractException(
                    "invalid_envelope",
                    $"Legacy mode rejects reserved envelope field '{propertyName}'.");
            }
        }
    }

    public static void EnsureEnvelopeObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope must be a JSON object.");
    }

    public static int ReadAndValidateSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out var schemaVersionProperty))
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope schemaVersion is required.");

        if (schemaVersionProperty.ValueKind != JsonValueKind.Number
            || !schemaVersionProperty.TryGetInt32(out var schemaVersion))
        {
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope schemaVersion must be an integer.");
        }

        if (schemaVersion != TelemetryEventEnvelopeV1.SupportedSchemaVersion)
        {
            throw new TelemetryKafkaContractException(
                "unsupported_schema_version",
                $"Unsupported schema version {schemaVersion}.");
        }

        return schemaVersion;
    }

    public static void EnsureEnvelopeRequiredMetadataPresent(JsonElement root)
    {
        if (!root.TryGetProperty("eventId", out var eventIdProperty))
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope eventId is required.");

        if (eventIdProperty.ValueKind != JsonValueKind.String
            || !Guid.TryParse(eventIdProperty.GetString(), out var eventId)
            || eventId == Guid.Empty)
        {
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope eventId is required.");
        }

        if (!root.TryGetProperty("occurredAt", out var occurredAtProperty))
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope occurredAt is required.");

        if (occurredAtProperty.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(
                occurredAtProperty.GetString(),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var occurredAt)
            || occurredAt == default)
        {
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope occurredAt is required.");
        }

        if (!root.TryGetProperty("eventType", out var eventTypeProperty))
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope eventType is required.");

        if (eventTypeProperty.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(eventTypeProperty.GetString()))
        {
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope eventType is required.");
        }

        if (!root.TryGetProperty("payload", out var payloadProperty))
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope payload is required.");

        if (payloadProperty.ValueKind == JsonValueKind.Null)
            throw new TelemetryKafkaContractException("null_payload", "Envelope payload is null.");
    }
}
