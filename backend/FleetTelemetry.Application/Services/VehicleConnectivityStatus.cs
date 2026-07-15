namespace FleetTelemetry.Application.Services;

public static class VehicleConnectivityStatus
{
    public const string Online = "online";
    public const string Offline = "offline";

    public static string Resolve(
        DateTimeOffset lastTimestamp,
        DateTimeOffset now,
        int onlineThresholdMinutes) =>
        Resolve(lastTimestamp, now, TimeSpan.FromMinutes(onlineThresholdMinutes));

    public static string Resolve(
        DateTimeOffset lastTimestamp,
        DateTimeOffset now,
        TimeSpan onlineWindow)
    {
        var onlineThreshold = now - onlineWindow;
        return lastTimestamp >= onlineThreshold ? Online : Offline;
    }
}
