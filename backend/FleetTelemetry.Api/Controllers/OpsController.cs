using FleetTelemetry.Api.Filters;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

// Endpoints operativos de diagnóstico (MVP / sustentación).
[ApiController]
[Route("api/ops")]
public class OpsController : ControllerBase
{
    private readonly IOpsQueryService _opsQueryService;

    public OpsController(IOpsQueryService opsQueryService)
    {
        _opsQueryService = opsQueryService;
    }

    // Resumen operativo; protegido si Auth.Enabled=true.
    [HttpGet("summary")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.OperationsRead)]
    public async Task<ActionResult<OpsSummaryResponse>> Summary(CancellationToken cancellationToken)
    {
        var summary = await _opsQueryService.GetSummaryAsync(cancellationToken);
        return Ok(summary);
    }
}
