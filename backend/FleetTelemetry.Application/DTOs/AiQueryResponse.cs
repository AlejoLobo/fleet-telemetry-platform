// DTO de respuesta del agente de IA.
namespace FleetTelemetry.Application.DTOs;

// Respuesta generada y fuentes consultadas.
public record AiQueryResponse(
    string Answer,
    IReadOnlyList<string> Sources);
