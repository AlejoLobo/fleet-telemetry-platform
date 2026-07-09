using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

// Controlador de consultas al agente de IA operativo.
namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly IAiAgentService _aiAgentService;

    public AiController(IAiAgentService aiAgentService)
    {
        _aiAgentService = aiAgentService;
    }

    // Envía pregunta al agente y devuelve respuesta con fuentes.
    [HttpPost("query")]
    public async Task<ActionResult<AiQueryResponse>> Query(
        [FromBody] AiQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "La pregunta es obligatoria." });

        var response = await _aiAgentService.QueryAsync(request, cancellationToken);
        return Ok(response);
    }
}
