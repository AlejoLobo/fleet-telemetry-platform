using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetTelemetry.Infrastructure.Realtime;

// Mensaje publicado por el Worker en fleet.realtime y consumido por la API.
public sealed class FleetRealtimeKafkaMessage
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; init; }

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(FleetRealtimeKafkaMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public static FleetRealtimeKafkaMessage Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FleetRealtimeKafkaMessage>(json, Options)
                ?? throw new RealtimeKafkaInvalidPayloadException("Unable to deserialize fleet realtime message.");
        }
        catch (JsonException ex)
        {
            throw new RealtimeKafkaInvalidPayloadException("Invalid fleet realtime JSON payload.", ex);
        }
    }
}
