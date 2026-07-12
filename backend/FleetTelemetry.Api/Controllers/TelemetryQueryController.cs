using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Api.Filters;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// Parte de consulta histórica de telemetría por vehículo.
namespace FleetTelemetry.Api.Controllers;

public partial class TelemetryController
{
    [HttpGet("{vehicleId}")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public async Task<ActionResult<CursorPage<TelemetryEventResponse>>> GetByVehicle(
        string vehicleId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] ITelemetryRepository telemetryRepository,
        [FromServices] TimeProvider timeProvider,
        [FromServices] IOptions<QueryLimitsOptions> queryLimits,
        CancellationToken cancellationToken)
    {
        var limits = queryLimits.Value;
        var resolvedPageSize = pageSize ?? limits.HistoryDefaultPageSize;
        if (resolvedPageSize < 1 || resolvedPageSize > limits.HistoryMaxPageSize)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "pageSize inválido.",
                $"pageSize debe estar entre 1 y {limits.HistoryMaxPageSize}."));
        }

        DateTimeOffset toValue;
        DateTimeOffset fromValue;

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            try
            {
                var cursorPayload = CursorCodec.Decode<TelemetryHistoryCursorPayload>(cursor);
                CursorValidators.ValidateHistoryCursor(
                    cursorPayload,
                    vehicleId,
                    cursorPayload.From,
                    cursorPayload.To,
                    limits.HistoryMaxRangeDays);

                if (from.HasValue && from.Value != cursorPayload.From)
                {
                    return BadRequest(CreateProblem(
                        StatusCodes.Status400BadRequest,
                        "Parámetros inválidos.",
                        "from no coincide con el cursor."));
                }

                if (to.HasValue && to.Value != cursorPayload.To)
                {
                    return BadRequest(CreateProblem(
                        StatusCodes.Status400BadRequest,
                        "Parámetros inválidos.",
                        "to no coincide con el cursor."));
                }

                toValue = cursorPayload.To;
                fromValue = cursorPayload.From;
            }
            catch (InvalidCursorException ex)
            {
                return BadRequest(CreateProblem(StatusCodes.Status400BadRequest, "Cursor inválido.", ex.Message));
            }
        }
        else
        {
            toValue = to ?? timeProvider.GetUtcNow();
            fromValue = from ?? toValue.AddHours(-24);
        }

        try
        {
            var page = await telemetryRepository.GetVehicleHistoryPageAsync(
                vehicleId,
                fromValue,
                toValue,
                resolvedPageSize,
                cursor,
                cancellationToken);

            var response = new CursorPage<TelemetryEventResponse>(
                page.Items.Select(e => new TelemetryEventResponse(
                    e.EventId,
                    e.VehicleId,
                    e.DriverId,
                    e.Timestamp,
                    e.Latitude,
                    e.Longitude,
                    e.SpeedKmh,
                    e.FuelLevelPercent,
                    e.BatteryPercent)).ToList(),
                page.NextCursor,
                page.HasMore);

            return Ok(response);
        }
        catch (InvalidCursorException ex)
        {
            return BadRequest(CreateProblem(StatusCodes.Status400BadRequest, "Cursor inválido.", ex.Message));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblem(StatusCodes.Status400BadRequest, "Parámetros inválidos.", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblem(StatusCodes.Status400BadRequest, "Parámetros inválidos.", ex.Message));
        }
    }

    private static ProblemDetails CreateProblem(int status, string title, string detail) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail
        };
}
