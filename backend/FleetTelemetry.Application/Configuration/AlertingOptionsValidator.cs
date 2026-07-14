using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Configuration;

public sealed class AlertingOptionsValidator : IValidateOptions<AlertingOptions>
{
    public ValidateOptionsResult Validate(string? name, AlertingOptions options)
    {
        if (options.CooldownSeconds <= 0)
            return ValidateOptionsResult.Fail("Alerting:CooldownSeconds must be greater than zero.");

        if (!double.IsFinite(options.OverspeedThresholdKmh) || options.OverspeedThresholdKmh < 0)
            return ValidateOptionsResult.Fail(
                "Alerting:OverspeedThresholdKmh must be a finite number >= 0.");

        if (!double.IsFinite(options.LowFuelPercent)
            || options.LowFuelPercent is < 0 or > 100)
        {
            return ValidateOptionsResult.Fail(
                "Alerting:LowFuelPercent must be a finite number between 0 and 100.");
        }

        if (!double.IsFinite(options.LowBatteryPercent)
            || options.LowBatteryPercent is < 0 or > 100)
        {
            return ValidateOptionsResult.Fail(
                "Alerting:LowBatteryPercent must be a finite number between 0 and 100.");
        }

        return ValidateOptionsResult.Success;
    }
}
