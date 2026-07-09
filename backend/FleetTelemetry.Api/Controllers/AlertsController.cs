using FleetTelemetry.Api.Filters;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IAlertRepository _alertRepository;
    private readonly AcknowledgeAlertUseCase _acknowledgeAlertUseCase;

    public AlertsController(
        IAlertRepository alertRepository,
        AcknowledgeAlertUseCase acknowledgeAlertUseCase)
    {
        _alertRepository = alertRepository;
        _acknowledgeAlertUseCase = acknowledgeAlertUseCase;
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

    [HttpPatch("{alertId:guid}/acknowledge")]
    [AuthorizeWhenEnabled]
    public async Task<IActionResult> Acknowledge(Guid alertId, CancellationToken cancellationToken)
    {
        var acknowledged = await _acknowledgeAlertUseCase.ExecuteAsync(alertId, cancellationToken);
        if (!acknowledged)
            return NotFound(new { error = $"Alerta '{alertId}' no encontrada o ya confirmada." });

        return Ok(new { message = "Alerta confirmada.", alertId });
    }
}
