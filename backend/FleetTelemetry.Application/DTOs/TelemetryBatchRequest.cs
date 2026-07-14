namespace FleetTelemetry.Application.DTOs;

public record TelemetryBatchRequest(
    IReadOnlyList<TelemetryEventRequest> Events);
