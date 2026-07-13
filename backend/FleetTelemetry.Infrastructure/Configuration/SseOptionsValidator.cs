using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Configuration;

public sealed class SseOptionsValidator : IValidateOptions<SseOptions>
{
    public ValidateOptionsResult Validate(string? name, SseOptions options) =>
        string.IsNullOrWhiteSpace(options.InstanceId)
            ? ValidateOptionsResult.Fail("Sse.InstanceId must not be empty after resolution.")
            : ValidateOptionsResult.Success;
}
