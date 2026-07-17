namespace FleetTelemetry.Application.Services;

public static class VehicleConnectivityStatus
{
    public const string Online = "online";
    public const string Offline = "offline";

    public static string Resolve(
        DateTimeOffset lastTimestamp,
        DateTimeOffset now,
        int onlineThresholdMinutes)
    {
        var onlineThreshold = now.AddMinutes(-onlineThresholdMinutes);
        return lastTimestamp >= onlineThreshold ? Online : Offline;
    }
}
