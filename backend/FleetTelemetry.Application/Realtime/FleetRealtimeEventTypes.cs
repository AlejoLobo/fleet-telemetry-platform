namespace FleetTelemetry.Application.Realtime;

public static class FleetRealtimeEventTypes
{
    public const string VehicleUpdate = "vehicle-update";
    public const string FleetUpdate = "fleet-update";
    public const string Alert = "alert";
    public const string Heartbeat = "heartbeat";
    public const string Connected = "connected";
    public const string StreamReset = "stream-reset";
}
