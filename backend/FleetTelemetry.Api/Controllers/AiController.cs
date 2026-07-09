using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

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
