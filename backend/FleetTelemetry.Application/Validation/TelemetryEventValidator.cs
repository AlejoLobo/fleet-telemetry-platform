using FleetTelemetry.Application.Common;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Validation;

// Fachada estática para compatibilidad con código existente.
public static class TelemetryEventValidator
{
    private static readonly ITelemetryEventValidator Validator = new TelemetryEventValidatorService();

    public static void Validate(TelemetryEventRequest request)
    {
        var result = Validator.ValidateAndMap(request);
        if (!result.IsSuccess)
            throw new ArgumentException(result.Errors[0].Message);
    }

    public static TelemetryEvent MapToDomain(TelemetryEventRequest request) =>
        Validator.ValidateAndMap(request).GetValueOrThrow();
}
