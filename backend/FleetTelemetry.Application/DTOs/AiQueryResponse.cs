namespace FleetTelemetry.Application.DTOs;

public record AiQueryResponse(
    string Answer,
    IReadOnlyList<string> Sources);
