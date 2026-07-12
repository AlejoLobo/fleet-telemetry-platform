namespace FleetTelemetry.Infrastructure.Realtime;

// Estado compartido entre ciclos del servicio de expiración.
public sealed class FleetConnectivityExpiryState
{
    public DateTimeOffset? PreviousOnlineThreshold { get; set; }
}
