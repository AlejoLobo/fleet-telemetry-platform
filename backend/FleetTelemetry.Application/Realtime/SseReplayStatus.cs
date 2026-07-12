namespace FleetTelemetry.Application.Realtime;

// Resultado explícito del replay local ante Last-Event-ID.
public enum SseReplayStatus
{
    ReplayAvailable,
    ReplayGap,
    LastEventIdAhead
}
