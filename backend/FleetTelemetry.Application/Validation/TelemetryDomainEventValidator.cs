using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Validation;

// Valida la entidad de dominio TelemetryEvent (p. ej. mensajes Kafka ya deserializados).
// Distinto de TelemetryEventValidator, que valida DTOs de la API.
public static class TelemetryDomainEventValidator
{
    public static void Validate(TelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        if (telemetryEvent.EventId == Guid.Empty)
            throw new ArgumentException("EventId is required.");

        if (string.IsNullOrWhiteSpace(telemetryEvent.VehicleId))
            throw new ArgumentException("VehicleId is required.");

        if (telemetryEvent.Timestamp == default)
            throw new ArgumentException("Timestamp is required.");

        if (telemetryEvent.Latitude is < -90 or > 90)
            throw new ArgumentException("Latitude must be between -90 and 90.");

        if (telemetryEvent.Longitude is < -180 or > 180)
            throw new ArgumentException("Longitude must be between -180 and 180.");

        if (telemetryEvent.SpeedKmh < 0)
            throw new ArgumentException("SpeedKmh must be >= 0.");
    }
}
