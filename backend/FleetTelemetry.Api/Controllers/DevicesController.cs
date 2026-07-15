using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Api.Filters;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly RegisterDeviceUseCase _registerDeviceUseCase;
    private readonly RenameDeviceUseCase _renameDeviceUseCase;

    public DevicesController(
        RegisterDeviceUseCase registerDeviceUseCase,
        RenameDeviceUseCase renameDeviceUseCase)
    {
        _registerDeviceUseCase = registerDeviceUseCase;
        _renameDeviceUseCase = renameDeviceUseCase;
    }

    [HttpPost("register")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.TelemetryWrite)]
    public async Task<ActionResult<DeviceResponse>> Register(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var device = await _registerDeviceUseCase.ExecuteAsync(request, cancellationToken);
            return Ok(ToResponse(device));
        }
        catch (InvalidDeviceIdException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{deviceId:guid}/name")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.TelemetryWrite)]
    public async Task<ActionResult<DeviceResponse>> Rename(
        Guid deviceId,
        [FromBody] RenameDeviceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var device = await _renameDeviceUseCase.ExecuteAsync(deviceId, request, cancellationToken);
            return Ok(ToResponse(device));
        }
        catch (InvalidDeviceIdException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidVehicleNameException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (DeviceNotFoundException)
        {
            return NotFound(new { error = $"Device '{deviceId}' was not found." });
        }
        catch (VehicleNameConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    private static DeviceResponse ToResponse(FleetDevice device) =>
        new(device.DeviceId, device.VehicleName);
}
