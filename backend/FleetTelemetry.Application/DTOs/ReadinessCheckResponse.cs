namespace FleetTelemetry.Application.DTOs;

public record ReadinessCheckResponse(
    string Status,
    string Service,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Checks);
