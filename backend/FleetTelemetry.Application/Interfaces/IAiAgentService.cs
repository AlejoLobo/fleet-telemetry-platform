using FleetTelemetry.Application.DTOs;

// Contrato del servicio de agente de IA operativo.
namespace FleetTelemetry.Application.Interfaces;

// Ejecuta consultas en lenguaje natural sobre la flota.
public interface IAiAgentService
{
    // Procesa una pregunta y devuelve respuesta con fuentes.
    Task<AiQueryResponse> QueryAsync(AiQueryRequest request, CancellationToken cancellationToken = default);
}
