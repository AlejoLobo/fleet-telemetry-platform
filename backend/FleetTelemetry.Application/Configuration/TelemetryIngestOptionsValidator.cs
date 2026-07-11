using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Configuration;

public sealed class TelemetryIngestOptionsValidator : IValidateOptions<TelemetryIngestOptions>
{
    public ValidateOptionsResult Validate(string? name, TelemetryIngestOptions options)
    {
        if (options.MaxBatchSize < 1 || options.MaxBatchSize > 500)
            return ValidateOptionsResult.Fail("TelemetryIngest:MaxBatchSize debe estar entre 1 y 500.");

        if (options.MaxPayloadBytes < 1024 || options.MaxPayloadBytes > 1_048_576)
            return ValidateOptionsResult.Fail("TelemetryIngest:MaxPayloadBytes debe estar entre 1024 y 1048576.");

        if (options.MaxVehicleIdLength < 1 || options.MaxVehicleIdLength > 128)
            return ValidateOptionsResult.Fail("TelemetryIngest:MaxVehicleIdLength inválido.");

        if (options.MaxDriverIdLength < 1 || options.MaxDriverIdLength > 128)
            return ValidateOptionsResult.Fail("TelemetryIngest:MaxDriverIdLength inválido.");

        if (options.MaxFutureSkewMinutes < 0 || options.MaxFutureSkewMinutes > 60)
            return ValidateOptionsResult.Fail("TelemetryIngest:MaxFutureSkewMinutes inválido.");

        if (options.MaxPastSkewDays < 1 || options.MaxPastSkewDays > 365)
            return ValidateOptionsResult.Fail("TelemetryIngest:MaxPastSkewDays inválido.");

        if (options.MaxSpeedKmh <= 0 || options.MaxSpeedKmh > 500)
            return ValidateOptionsResult.Fail("TelemetryIngest:MaxSpeedKmh debe estar entre 0 y 500.");

        return ValidateOptionsResult.Success;
    }
}
