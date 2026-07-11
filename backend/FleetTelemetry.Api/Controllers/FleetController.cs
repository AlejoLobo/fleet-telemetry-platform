using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Api.Filters;
using FleetTelemetry.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;

// Controlador de consulta de estado de flota.
namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/fleet")]
public class FleetController : ControllerBase
{
    private readonly IFleetQueryService _fleetQueryService;

    public FleetController(IFleetQueryService fleetQueryService)
    {
        _fleetQueryService = fleetQueryService;
    }

    // Lista último estado de todos los vehículos.
    [HttpGet]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public async Task<ActionResult<IReadOnlyList<VehicleLatestStatusResponse>>> GetAll(
        [FromQuery] bool liveOnly = false,
        CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetQueryService.GetLatestVehicleStatusesAsync(liveOnly, cancellationToken);
        return Ok(vehicles);
    }

    // Obtiene estado de un vehículo por identificador.
    [HttpGet("{vehicleId}")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public async Task<ActionResult<VehicleLatestStatusResponse>> GetById(
        string vehicleId,
        CancellationToken cancellationToken)
    {
        var vehicle = await _fleetQueryService.GetVehicleStatusAsync(vehicleId, cancellationToken);
        if (vehicle is null)
            return NotFound(new { error = $"Vehículo '{vehicleId}' no encontrado." });

        return Ok(vehicle);
    }
}
