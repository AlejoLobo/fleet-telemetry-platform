using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

public partial class TelemetryController
{
    [HttpGet("{vehicleId}")]
    public async Task<ActionResult<IReadOnlyList<TelemetryEventResponse>>> GetByVehicle(
        string vehicleId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromServices] ITelemetryRepository telemetryRepository,
        CancellationToken cancellationToken)
    {
        var toValue = to ?? DateTimeOffset.UtcNow;
        var fromValue = from ?? toValue.AddHours(-24);

        var events = await telemetryRepository.GetByVehicleAsync(vehicleId, fromValue, toValue, cancellationToken);

        var response = events.Select(e => new TelemetryEventResponse(
            e.EventId,
            e.VehicleId,
            e.DriverId,
            e.Timestamp,
            e.Latitude,
            e.Longitude,
            e.SpeedKmh,
            e.FuelLevelPercent,
            e.BatteryPercent)).ToList();

        return Ok(response);
    }
}
