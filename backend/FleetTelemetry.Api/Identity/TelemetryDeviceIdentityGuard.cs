using System.Security.Claims;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Identity;

/// <summary>
/// Valida coherencia entre payload.deviceId, X-Device-Id y claim device_id.
/// </summary>
public static class TelemetryDeviceIdentityGuard
{
    public static IActionResult? ValidateOrError(
        HttpContext httpContext,
        Guid payloadDeviceId)
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

        var deviceClaim = httpContext.User.FindFirstValue("device_id");
        if (!string.IsNullOrWhiteSpace(deviceClaim)
            && TryParseDeviceGuid(deviceClaim, out var claimId)
            && claimId != payloadDeviceId)
        {
            return new ObjectResult(new { error = "Authenticated device_id does not match payload deviceId." })
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
