using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Contracts;

// Contrato versionado de eventos Kafka con metadatos de trazabilidad.
public sealed class TelemetryEventEnvelope
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid EventId { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public string Source { get; init; } = "fleet-telemetry";
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public TelemetryEvent Payload { get; init; } = null!;

    public static TelemetryEventEnvelope Wrap(
        TelemetryEvent payload,
        string source = "fleet-telemetry",
        Guid? correlationId = null,
        Guid? causationId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new()
        {
            SchemaVersion = CurrentSchemaVersion,
            EventId = payload.EventId,
            CorrelationId = correlationId ?? payload.EventId,
            CausationId = causationId,
            OccurredAt = payload.Timestamp,
            ReceivedAt = DateTimeOffset.UtcNow,
            Source = source,
            Metadata = metadata,
            Payload = payload
        };
}
