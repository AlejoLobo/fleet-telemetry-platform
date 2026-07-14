using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Configuration;

public sealed class SseOptionsPostConfigure : IPostConfigureOptions<SseOptions>
{
    public void PostConfigure(string? name, SseOptions options) =>
        options.InstanceId = SseInstanceIdResolver.Resolve(options.InstanceId);
}
