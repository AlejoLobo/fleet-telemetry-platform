namespace FleetTelemetry.Application.Realtime;

// Contrato canónico de eventos realtime de flota.
public static class FleetRealtimeEventTypes
{
    public const string VehicleUpdate = "vehicle-update";
    public const string FleetUpdate = "fleet-update";
    public const string Alert = "alert";
    public const string Heartbeat = "heartbeat";
    public const string Connected = "connected";
    public const string StreamReset = "stream-reset";
}
