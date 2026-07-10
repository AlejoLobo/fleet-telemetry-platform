using System.Text.Json;
using FleetTelemetry.Domain.Entities;

// Serialización JSON de eventos de telemetría.
namespace FleetTelemetry.Infrastructure.Kafka;

// Convierte entre TelemetryEvent y JSON camelCase.
public static class TelemetryEventJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Serializa evento a JSON.
    public static string Serialize(TelemetryEvent telemetryEvent) =>
        JsonSerializer.Serialize(telemetryEvent, Options);

    // Deserializa JSON a entidad de dominio.
    // No aplica reglas de negocio: el Worker debe llamar a TelemetryDomainEventValidator.Validate después.
    public static TelemetryEvent Deserialize(string json) =>
        JsonSerializer.Deserialize<TelemetryEvent>(json, Options)
        ?? throw new InvalidOperationException("Unable to deserialize telemetry event.");
}
