using FleetTelemetry.Application.Common;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Validation;

// Fachada estática para compatibilidad con código existente.
public static class TelemetryDomainEventValidator
{
    private static readonly ITelemetryDomainEventValidator Validator = new TelemetryDomainEventValidatorService();

    public static void Validate(TelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        var result = Validator.Validate(telemetryEvent);
        if (!result.IsSuccess)
            throw new ArgumentException(result.Errors[0].Message);
    }
}
