using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Api.Filters;
using FleetTelemetry.Api.Identity;
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
    private readonly UpdateDeviceProfileUseCase _updateDeviceProfileUseCase;

    public DevicesController(
        RegisterDeviceUseCase registerDeviceUseCase,
        RenameDeviceUseCase renameDeviceUseCase,
        UpdateDeviceProfileUseCase updateDeviceProfileUseCase)
    {
        _registerDeviceUseCase = registerDeviceUseCase;
        _renameDeviceUseCase = renameDeviceUseCase;
        _updateDeviceProfileUseCase = updateDeviceProfileUseCase;
    }

    [HttpPost("register")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.TelemetryWrite)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        // Registro: solo token de dispositivo (telemetry:write + device_id coincidente).
        var identityError = TelemetryDeviceIdentityGuard.ValidateOrError(
            HttpContext,
            request.DeviceId,
            DeviceIdentityRequirement.RequireMatchingDeviceClaim);
        if (identityError is not null)
            return identityError;

        try
        {
            var device = await _registerDeviceUseCase.ExecuteAsync(request, cancellationToken);
            return Ok(ToResponse(device));
        }
        catch (InvalidDeviceIdException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidVehicleTypeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{deviceId:guid}/profile")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.DeviceRename)]
    public async Task<IActionResult> UpdateProfile(
        Guid deviceId,
        [FromBody] UpdateDeviceProfileRequest request,
        CancellationToken cancellationToken)
    {
        var identityError = TelemetryDeviceIdentityGuard.ValidateOrError(
            HttpContext,
            deviceId,
            DeviceIdentityRequirement.AllowDeviceManageBypass);
        if (identityError is not null)
            return identityError;

        try
        {
            var device = await _updateDeviceProfileUseCase.ExecuteAsync(deviceId, request, cancellationToken);
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
        catch (InvalidVehicleTypeException ex)
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

    [HttpPatch("{deviceId:guid}/name")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.DeviceRename)]
    public async Task<IActionResult> Rename(
        Guid deviceId,
        [FromBody] RenameDeviceRequest request,
        CancellationToken cancellationToken)
    {
        // Compatibilidad: delega en la misma lógica de perfil (solo nombre).
        var identityError = TelemetryDeviceIdentityGuard.ValidateOrError(
            HttpContext,
            deviceId,
            DeviceIdentityRequirement.AllowDeviceManageBypass);
        if (identityError is not null)
            return identityError;

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
        new(device.DeviceId, device.VehicleName, device.VehicleType);
}
