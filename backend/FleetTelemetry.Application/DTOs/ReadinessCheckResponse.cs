namespace FleetTelemetry.Application.DTOs;

// Resultado de readiness sin secretos ni connection strings.
public record ReadinessCheckResponse(
    string Status,
    string Service,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Checks);
