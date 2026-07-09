using System.Text.Json;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Infrastructure.Kafka;

public static class TelemetryEventJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(TelemetryEvent telemetryEvent) =>
        JsonSerializer.Serialize(telemetryEvent, Options);

    public static TelemetryEvent Deserialize(string json) =>
        JsonSerializer.Deserialize<TelemetryEvent>(json, Options)
        ?? throw new InvalidOperationException("Unable to deserialize telemetry event.");
}
