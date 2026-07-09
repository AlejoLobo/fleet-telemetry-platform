namespace FleetTelemetry.Application.DTOs;

public record FleetSseEvent(
    string EventType,
    object Data,
    DateTimeOffset Timestamp);
