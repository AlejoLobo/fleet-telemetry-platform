using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly IngestTelemetryEventUseCase _ingestEventUseCase;
    private readonly IngestTelemetryBatchUseCase _ingestBatchUseCase;

    public TelemetryController(
        IngestTelemetryEventUseCase ingestEventUseCase,
        IngestTelemetryBatchUseCase ingestBatchUseCase)
    {
        _ingestEventUseCase = ingestEventUseCase;
        _ingestBatchUseCase = ingestBatchUseCase;
    }

    [HttpPost]
    public async Task<IActionResult> IngestSingle(
        [FromBody] TelemetryEventRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ingestEventUseCase.ExecuteAsync(request, cancellationToken);
            return Accepted(new { message = "Telemetry event accepted for processing." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("batch")]
    public async Task<IActionResult> IngestBatch(
        [FromBody] TelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ingestBatchUseCase.ExecuteAsync(request, cancellationToken);
            return Accepted(new { message = "Telemetry batch accepted for processing." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
