using System.Text.Json;
using FleetTelemetry.Application.Realtime;

namespace FleetTelemetry.Infrastructure.Realtime;

// Valida el contrato Kafka de fleet.realtime antes de publicar en el broker SSE.
internal static class FleetRealtimeKafkaMessageValidator
{
    private static readonly HashSet<string> SupportedEventTypes =
    [
        FleetRealtimeEventTypes.VehicleUpdate,
        FleetRealtimeEventTypes.FleetUpdate,
        FleetRealtimeEventTypes.Alert
    ];

    public static void Validate(FleetRealtimeKafkaMessage message)
    {
        if (message.SchemaVersion is null)
        {
            throw new RealtimeKafkaInvalidPayloadException("schemaVersion is required.");
        }

        if (message.SchemaVersion != FleetRealtimeKafkaMessage.CurrentSchemaVersion)
        {
            throw new RealtimeKafkaInvalidPayloadException(
                $"Unsupported schemaVersion {message.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(message.EventType)
            || !SupportedEventTypes.Contains(message.EventType))
        {
            throw new RealtimeKafkaInvalidPayloadException(
                $"Unsupported or missing eventType '{message.EventType}'.");
        }

        if (message.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new RealtimeKafkaInvalidPayloadException("Payload must not be null or undefined.");
        }

        if (message.OccurredAt == default)
        {
            throw new RealtimeKafkaInvalidPayloadException("OccurredAt must be a valid timestamp.");
        }

        switch (message.EventType)
        {
            case FleetRealtimeEventTypes.VehicleUpdate:
                ValidateVehiclePayload(message.Payload);
                break;
            case FleetRealtimeEventTypes.Alert:
                ValidateAlertPayload(message.Payload);
                break;
            case FleetRealtimeEventTypes.FleetUpdate:
                ValidateFleetUpdatePayload(message.Payload);
                break;
        }
    }

    private static void ValidateVehiclePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new RealtimeKafkaInvalidPayloadException("vehicle-update payload must be an object.");
        }

        RequireNonEmptyString(payload, "vehicleId");
        RequireNonEmptyString(payload, "name");
        RequireNonEmptyString(payload, "status");
        RequireTemporalProperty(payload, "lastSeenAt");
    }

    private static void ValidateAlertPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new RealtimeKafkaInvalidPayloadException("alert payload must be an object.");
        }

        RequireNonEmptyString(payload, "alertId");
        RequireNonEmptyString(payload, "vehicleId");
        RequireNonEmptyString(payload, "alertType");
        RequireNonEmptyString(payload, "severity");
        RequireNonEmptyString(payload, "message");
        RequireTemporalProperty(payload, "createdAt");
        RequireBooleanProperty(payload, "isAcknowledged");
    }

    private static void ValidateFleetUpdatePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Array)
        {
            throw new RealtimeKafkaInvalidPayloadException("fleet-update payload must be an array.");
        }

        var index = 0;
        foreach (var item in payload.EnumerateArray())
        {
            try
            {
                ValidateVehiclePayload(item);
            }
            catch (RealtimeKafkaInvalidPayloadException ex)
            {
                throw new RealtimeKafkaInvalidPayloadException(
                    $"fleet-update element[{index}] invalid: {ex.Message}",
                    ex);
            }

            index++;
        }
    }

    private static void RequireNonEmptyString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new RealtimeKafkaInvalidPayloadException(
                $"Payload missing required property '{propertyName}'.");
        }
    }

    private static void RequireBooleanProperty(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property)
            || (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            throw new RealtimeKafkaInvalidPayloadException(
                $"Payload missing required boolean property '{propertyName}'.");
        }
    }

    private static void RequireTemporalProperty(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(property.GetString(), out _))
        {
            throw new RealtimeKafkaInvalidPayloadException(
                $"Payload missing required temporal property '{propertyName}'.");
        }
    }
}
