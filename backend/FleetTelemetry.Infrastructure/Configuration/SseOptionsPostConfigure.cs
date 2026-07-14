using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Configuration;

// Aplica identidad de proceso cuando no hay InstanceId explícito en configuración.
public sealed class SseOptionsPostConfigure : IPostConfigureOptions<SseOptions>
{
    public void PostConfigure(string? name, SseOptions options) =>
        options.InstanceId = SseInstanceIdResolver.Resolve(options.InstanceId);
}
