using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Configuration;

public sealed class QueryLimitsOptionsValidator : IValidateOptions<QueryLimitsOptions>
{
    public ValidateOptionsResult Validate(string? name, QueryLimitsOptions options)
    {
        if (options.FleetDefaultPageSize < 1 || options.FleetMaxPageSize < options.FleetDefaultPageSize)
            return ValidateOptionsResult.Fail("Fleet page size limits are invalid.");

        if (options.HistoryDefaultPageSize < 1 || options.HistoryMaxPageSize < options.HistoryDefaultPageSize)
            return ValidateOptionsResult.Fail("History page size limits are invalid.");

        if (options.HistoryMaxRangeDays < 1)
            return ValidateOptionsResult.Fail("HistoryMaxRangeDays must be at least 1.");

        if (options.OnlineThresholdMinutes < 1)
            return ValidateOptionsResult.Fail("OnlineThresholdMinutes must be at least 1.");

        if (options.OnlineThresholdSeconds < 0)
            return ValidateOptionsResult.Fail("OnlineThresholdSeconds must be zero or positive.");

        if (options.OnlineThresholdSeconds > 0 && options.OnlineThresholdSeconds < 15)
            return ValidateOptionsResult.Fail("OnlineThresholdSeconds must be at least 15 when enabled.");

        return ValidateOptionsResult.Success;
    }
}
