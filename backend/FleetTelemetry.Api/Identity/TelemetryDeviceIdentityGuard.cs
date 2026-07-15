using System.Security.Claims;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.ValueObjects;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Identity;

public enum DeviceIdentityRequirement
{
    /// <summary>Ingesta/registro: con auth requiere claim device_id coincidente.</summary>
    RequireMatchingDeviceClaim = 0,

    /// <summary>Rename: claim coincidente O permiso device:manage.</summary>
    AllowDeviceManageBypass = 1
}

/// <summary>
/// Valida coherencia entre deviceId (payload/ruta), X-Device-Id y claim device_id.
/// </summary>
public static class TelemetryDeviceIdentityGuard
{
    public static IActionResult? ValidateOrError(
        HttpContext httpContext,
        Guid payloadDeviceId,
        DeviceIdentityRequirement requirement = DeviceIdentityRequirement.RequireMatchingDeviceClaim)
    {
        if (payloadDeviceId == Guid.Empty)
            return new BadRequestObjectResult(new { error = "DeviceId is required." });

        var payloadStorage = payloadDeviceId.ToString("D");

        if (httpContext.Request.Headers.TryGetValue("X-Device-Id", out var headerValues))
        {
            var rawHeader = headerValues.ToString();
            if (!string.IsNullOrWhiteSpace(rawHeader))
            {
                if (!TryNormalizeHeaderDeviceId(rawHeader, out var headerId))
                    return new BadRequestObjectResult(new { error = "X-Device-Id is invalid." });

                if (!string.Equals(headerId, payloadStorage, StringComparison.OrdinalIgnoreCase))
                {
                    return new BadRequestObjectResult(new
                    {
                        error = "X-Device-Id does not match payload deviceId."
                    });
                }
            }
        }

        var authEnabled = IsAuthEnabled(httpContext);

        // Operador con device:manage puede renombrar cualquier dispositivo (sin exigir device_id).
        if (requirement == DeviceIdentityRequirement.AllowDeviceManageBypass
            && HasPermission(httpContext.User, AuthorizationPermissions.DeviceManage))
        {
            return null;
        }

        var deviceClaim = httpContext.User.FindFirstValue(AuthorizationPermissions.DeviceIdClaimType);
        if (!string.IsNullOrWhiteSpace(deviceClaim))
        {
            if (!TryParseDeviceGuid(deviceClaim, out var claimId))
            {
                return new ObjectResult(new { error = "Authenticated device_id is invalid." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            if (claimId != payloadDeviceId)
            {
                return new ObjectResult(new { error = "Authenticated device_id does not match payload deviceId." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            return null;
        }

        // Con auth: telemetría/registro (y rename sin device:manage) exigen device_id en el JWT.
        if (authEnabled)
        {
            return new ObjectResult(new { error = "Authenticated device_id claim is required." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        return null;
    }

    public static IActionResult? ValidateBatchOrError(
        HttpContext httpContext,
        IReadOnlyList<TelemetryEventRequest> events)
    {
        if (events.Count == 0)
            return new BadRequestObjectResult(new { error = "Batch must contain at least one event." });

        var first = events[0].DeviceId;
        foreach (var evt in events)
        {
            if (evt.DeviceId != first)
            {
                return new BadRequestObjectResult(new
                {
                    error = "All events in a batch must share the same deviceId."
                });
            }
        }

        return ValidateOrError(httpContext, first);
    }

    private static bool IsAuthEnabled(HttpContext httpContext)
    {
        var configuration = httpContext.RequestServices.GetService<IConfiguration>();
        return configuration?.GetSection(AuthOptions.SectionName).GetValue<bool>("Enabled") == true;
    }

    private static bool HasPermission(ClaimsPrincipal user, string permission) =>
        user.HasClaim(AuthorizationPermissions.ClaimType, permission);

    private static bool TryNormalizeHeaderDeviceId(string raw, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParseDeviceGuid(raw, out var guid))
            return false;

        if (!DeviceId.TryCreate(guid, out _, out _))
            return false;

        normalized = guid.ToString("D");
        return true;
    }

    private static bool TryParseDeviceGuid(string raw, out Guid guid) =>
        Guid.TryParse(raw.Trim(), out guid) && guid != Guid.Empty;
}
