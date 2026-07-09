using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IAlertRepository _alertRepository;

    public AlertsController(IAlertRepository alertRepository)
    {
        _alertRepository = alertRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FleetAlertResponse>>> GetOpen(
        CancellationToken cancellationToken)
    {
        var alerts = await _alertRepository.GetOpenAlertsAsync(cancellationToken);

        var response = alerts.Select(a => new FleetAlertResponse(
            a.AlertId,
            a.VehicleId,
            a.AlertType,
            a.Severity,
            a.Message,
            a.CreatedAt,
            a.IsAcknowledged)).ToList();

        return Ok(response);
    }
}
