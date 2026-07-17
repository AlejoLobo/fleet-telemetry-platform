using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Api.Filters;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/fleet")]
public class FleetController : ControllerBase
{
    private readonly IFleetQueryService _fleetQueryService;
    private readonly QueryLimitsOptions _queryLimits;

    public FleetController(IFleetQueryService fleetQueryService, IOptions<QueryLimitsOptions> queryLimits)
    {
        _fleetQueryService = fleetQueryService;
        _queryLimits = queryLimits.Value;
    }

    [HttpGet]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public async Task<ActionResult<CursorPage<VehicleLatestStatusResponse>>> GetAll(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromQuery] bool liveOnly = false,
        [FromQuery] bool excludeSimulated = true,
        CancellationToken cancellationToken = default)
    {
        var resolvedPageSize = pageSize ?? _queryLimits.FleetDefaultPageSize;
        if (resolvedPageSize < 1 || resolvedPageSize > _queryLimits.FleetMaxPageSize)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "pageSize inválido.",
                $"pageSize debe estar entre 1 y {_queryLimits.FleetMaxPageSize}."));
        }

        try
        {
            var page = await _fleetQueryService.GetFleetPageAsync(
                resolvedPageSize,
                cursor,
                liveOnly,
                excludeSimulated,
                cancellationToken);
            return Ok(page);
        }
        catch (InvalidCursorException ex)
        {
            return BadRequest(CreateProblem(StatusCodes.Status400BadRequest, "Cursor inválido.", ex.Message));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblem(StatusCodes.Status400BadRequest, "Parámetros inválidos.", ex.Message));
        }
    }

    [HttpGet("{deviceId:guid}")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public async Task<ActionResult<VehicleLatestStatusResponse>> GetById(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var vehicle = await _fleetQueryService.GetVehicleStatusAsync(deviceId, cancellationToken);
        if (vehicle is null)
            return NotFound(new { error = $"Dispositivo '{deviceId:D}' no encontrado." });

        return Ok(vehicle);
    }

    private ProblemDetails CreateProblem(int status, string title, string detail) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail
        };
}
