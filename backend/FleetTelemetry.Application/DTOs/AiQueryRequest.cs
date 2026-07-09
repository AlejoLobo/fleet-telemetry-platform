// DTO de entrada para consultas al agente de IA.
namespace FleetTelemetry.Application.DTOs;

// Pregunta en lenguaje natural y sesión opcional.
public record AiQueryRequest(
    string Question,
    string? SessionId = null);
