namespace FleetTelemetry.Application.DTOs;

public record AiQueryRequest(
    string Question,
    string? SessionId = null);
