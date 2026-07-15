using FleetTelemetry.Application.Common;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Validation;

// Validación inyectable de eventos ya deserializados (p. ej. desde Kafka).
public interface ITelemetryDomainEventValidator
{
    Result<TelemetryEvent> Validate(TelemetryEvent telemetryEvent);
}

public sealed class TelemetryDomainEventValidatorService : ITelemetryDomainEventValidator
{
    public Result<TelemetryEvent> Validate(TelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        if (!TelemetryEvent.TryCreate(
                telemetryEvent.EventId,
                telemetryEvent.DeviceId,
                telemetryEvent.DriverId,
                telemetryEvent.Timestamp,
                telemetryEvent.Latitude,
                telemetryEvent.Longitude,
                telemetryEvent.SpeedKmh,
                telemetryEvent.FuelLevelPercent,
                telemetryEvent.BatteryPercent,
                out var validated,
                out var error,
                locationSource: telemetryEvent.LocationSource))
        {
            return Result<TelemetryEvent>.Failure(
                new ValidationError("domain_event_invalid", error ?? "Invalid telemetry event."));
        }

        return Result<TelemetryEvent>.Success(validated!);
    }
}
