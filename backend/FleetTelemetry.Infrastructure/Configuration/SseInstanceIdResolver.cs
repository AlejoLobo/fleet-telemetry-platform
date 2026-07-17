namespace FleetTelemetry.Infrastructure.Configuration;

public static class SseInstanceIdResolver
{
    public static string Resolve(string? configuredInstanceId)
    {
        if (!string.IsNullOrWhiteSpace(configuredInstanceId))
            return configuredInstanceId.Trim();

        var hostname = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrWhiteSpace(hostname))
            return hostname.Trim();

        var machineName = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machineName))
            return machineName.Trim();

        return string.Empty;
    }
}
