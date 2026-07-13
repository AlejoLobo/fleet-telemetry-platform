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
                RequireStringProperty(message.Payload, "vehicleId");
                break;
            case FleetRealtimeEventTypes.Alert:
                RequireStringProperty(message.Payload, "vehicleId");
                RequireStringProperty(message.Payload, "alertType");
                break;
            case FleetRealtimeEventTypes.FleetUpdate:
                if (message.Payload.ValueKind != JsonValueKind.Array)
                {
                    throw new RealtimeKafkaInvalidPayloadException(
                        "fleet-update payload must be an array.");
                }
                break;
        }
    }

    private static void RequireStringProperty(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new RealtimeKafkaInvalidPayloadException(
                $"Payload missing required property '{propertyName}'.");
        }
    }
}
