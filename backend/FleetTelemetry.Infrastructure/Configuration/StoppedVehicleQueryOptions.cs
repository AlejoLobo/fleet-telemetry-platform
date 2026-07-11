using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Configuration;

// Umbrales configurables para detección de vehículos detenidos.
public class StoppedVehicleQueryOptions
{
    public const string SectionName = "StoppedVehicles";

    public int LookbackHours { get; set; } = 48;
    public int VehicleFreshnessMinutes { get; set; } = 30;
    public int MaximumTelemetryGapMinutes { get; set; } = 10;
    public double StoppedSpeedThresholdKmh { get; set; } = 1;
}

// Valida opciones al arranque para evitar umbrales incoherentes.
public sealed class StoppedVehicleQueryOptionsValidator : IValidateOptions<StoppedVehicleQueryOptions>
{
    public ValidateOptionsResult Validate(string? name, StoppedVehicleQueryOptions options)
    {
        if (options.LookbackHours < 1 || options.LookbackHours > 168)
            return ValidateOptionsResult.Fail("StoppedVehicles:LookbackHours debe estar entre 1 y 168.");

        if (options.VehicleFreshnessMinutes < 1 || options.VehicleFreshnessMinutes > 1440)
            return ValidateOptionsResult.Fail("StoppedVehicles:VehicleFreshnessMinutes debe estar entre 1 y 1440.");

        if (options.MaximumTelemetryGapMinutes < 1 || options.MaximumTelemetryGapMinutes > 240)
            return ValidateOptionsResult.Fail("StoppedVehicles:MaximumTelemetryGapMinutes debe estar entre 1 y 240.");

        if (options.StoppedSpeedThresholdKmh <= 0 || options.StoppedSpeedThresholdKmh > 30)
            return ValidateOptionsResult.Fail("StoppedVehicles:StoppedSpeedThresholdKmh debe estar entre 0 y 30 km/h.");

        return ValidateOptionsResult.Success;
    }
}
