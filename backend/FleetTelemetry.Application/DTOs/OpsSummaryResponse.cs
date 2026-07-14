namespace FleetTelemetry.Application.DTOs;

public record OpsSummaryResponse(
    int TotalVehicles,
    int ActiveVehicles,
    int CriticalAlerts,
    DateTimeOffset? LastTelemetryAt,
    string SseMode,
    string TelemetryTopic,
    string DeadLetterTopic);
