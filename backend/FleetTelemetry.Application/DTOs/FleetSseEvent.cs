namespace FleetTelemetry.Application.DTOs;

public record FleetSseEvent(
    long StreamId,
    string EventType,
    object Data,
    DateTimeOffset Timestamp);
