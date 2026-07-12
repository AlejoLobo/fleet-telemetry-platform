using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Contracts;

// Envelope versionado V1 para Kafka. El payload es DTO, no entidad de dominio.
public sealed class TelemetryEventEnvelopeV1
{
    public const int SupportedSchemaVersion = 1;
    public const string TelemetryEventType = "fleet.telemetry.received";

    public int SchemaVersion { get; init; } = SupportedSchemaVersion;
    public string EventType { get; init; } = TelemetryEventType;
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public TelemetryEventPayloadV1 Payload { get; init; } = null!;

    public static TelemetryEventEnvelopeV1 FromDomain(TelemetryEvent telemetryEvent) => new()
    {
        SchemaVersion = SupportedSchemaVersion,
        EventType = TelemetryEventType,
        EventId = telemetryEvent.EventId,
        OccurredAt = telemetryEvent.Timestamp,
        Payload = TelemetryEventPayloadV1.FromDomain(telemetryEvent)
    };
}
