namespace FleetTelemetry.Application.DTOs;

// Resumen operativo para diagnóstico MVP / sustentación.
public record OpsSummaryResponse(
    int TotalVehicles,
    int ActiveVehicles,
    int CriticalAlerts,
    DateTimeOffset? LastTelemetryAt,
    string SseMode,
    string TelemetryTopic,
    string DeadLetterTopic);
