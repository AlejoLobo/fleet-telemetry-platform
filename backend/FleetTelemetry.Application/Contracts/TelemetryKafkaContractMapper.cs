using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Contracts;

public static class TelemetryKafkaContractMapper
{
    public static TelemetryEvent MapPayloadV1ToDomain(TelemetryEventPayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.EventId == Guid.Empty)
            throw new TelemetryKafkaContractException("invalid_envelope", "Payload EventId is required.");

        if (string.IsNullOrWhiteSpace(payload.VehicleId))
            throw new TelemetryKafkaContractException("invalid_envelope", "Payload VehicleId is required.");

        if (payload.Timestamp == default)
            throw new TelemetryKafkaContractException("invalid_envelope", "Payload timestamp is required.");

        if (!TelemetryEvent.TryCreate(
                payload.EventId,
                payload.VehicleId,
                payload.DriverId,
                payload.Timestamp,
                payload.Latitude,
                payload.Longitude,
                payload.SpeedKmh,
                payload.FuelLevelPercent,
                payload.BatteryPercent,
                out var telemetryEvent,
                out var error,
                locationSource: payload.LocationSource,
                vehicleName: payload.VehicleName))
        {
            throw new TelemetryKafkaContractException("invalid_domain", error ?? "Domain validation failed.");
        }

        return telemetryEvent!;
    }

    public static TelemetryEvent MapEnvelopeV1ToDomain(TelemetryEventEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.EventId == Guid.Empty)
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope EventId is required.");

        if (envelope.OccurredAt == default)
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope occurredAt is required.");

        if (string.IsNullOrWhiteSpace(envelope.EventType))
            throw new TelemetryKafkaContractException("invalid_envelope", "Envelope eventType is required.");

        if (!string.Equals(envelope.EventType, TelemetryEventEnvelopeV1.TelemetryEventType, StringComparison.Ordinal))
        {
            throw new TelemetryKafkaContractException(
                "unknown_event_type",
                $"Unknown event type '{envelope.EventType}'.");
        }

        if (envelope.Payload is null)
            throw new TelemetryKafkaContractException("null_payload", "Envelope payload is null.");

        if (envelope.EventId != envelope.Payload.EventId)
        {
            throw new TelemetryKafkaContractException(
                "invalid_envelope",
                "Envelope EventId does not match payload EventId.");
        }

        if (!RepresentsSameUtcInstant(envelope.OccurredAt, envelope.Payload.Timestamp))
        {
            throw new TelemetryKafkaContractException(
                "invalid_envelope",
                "Envelope occurredAt does not match payload timestamp.");
        }

        return MapPayloadV1ToDomain(envelope.Payload);
    }

    internal static bool RepresentsSameUtcInstant(DateTimeOffset left, DateTimeOffset right) =>
        left.ToUniversalTime().UtcDateTime == right.ToUniversalTime().UtcDateTime;
}
