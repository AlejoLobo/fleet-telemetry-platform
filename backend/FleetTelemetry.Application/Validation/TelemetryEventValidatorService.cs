using FleetTelemetry.Application.Common;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Validation;

// Implementación de reglas de ingesta con errores tipados.
public sealed class TelemetryEventValidatorService : ITelemetryEventValidator
{
    public Result<TelemetryEvent> ValidateAndMap(TelemetryEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TelemetryEvent.TryCreate(
                request.EventId,
                request.VehicleId,
                request.DriverId,
                request.Timestamp,
                request.Latitude,
                request.Longitude,
                request.SpeedKmh,
                request.FuelLevelPercent,
                request.BatteryPercent,
                out var telemetryEvent,
                out var error))
        {
            return Result<TelemetryEvent>.Failure(
                new ValidationError("telemetry_invalid", error ?? "Invalid telemetry event."));
        }

        return Result<TelemetryEvent>.Success(telemetryEvent!);
    }

    public Result<IReadOnlyList<TelemetryEvent>> ValidateAndMapBatch(IEnumerable<TelemetryEventRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var events = new List<TelemetryEvent>();
        var errors = new List<ValidationError>();
        var index = 0;

        foreach (var request in requests)
        {
            var result = ValidateAndMap(request);
            if (!result.IsSuccess)
            {
                foreach (var error in result.Errors)
                    errors.Add(new ValidationError($"batch_item_{index}_{error.Code}", error.Message));
            }
            else
            {
                events.Add(result.Value!);
            }

            index++;
        }

        return errors.Count > 0
            ? Result<IReadOnlyList<TelemetryEvent>>.Failure(errors)
            : Result<IReadOnlyList<TelemetryEvent>>.Success(events);
    }
}
