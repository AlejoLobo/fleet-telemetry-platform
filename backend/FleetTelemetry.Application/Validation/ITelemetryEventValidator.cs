using FleetTelemetry.Application.Common;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Validation;

// Validación inyectable de payloads de ingesta HTTP.
public interface ITelemetryEventValidator
{
    Result<TelemetryEvent> ValidateAndMap(TelemetryEventRequest request);
    Result<IReadOnlyList<TelemetryEvent>> ValidateAndMapBatch(IEnumerable<TelemetryEventRequest> requests);
}
