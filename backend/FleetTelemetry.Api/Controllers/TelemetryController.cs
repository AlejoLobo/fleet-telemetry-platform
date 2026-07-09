using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public partial class TelemetryController : ControllerBase
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
    [AuthorizeWhenEnabled]
    public async Task<IActionResult> IngestSingle(
        [FromBody] TelemetryEventRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ingestEventUseCase.ExecuteAsync(request, cancellationToken);
            return Accepted(new { message = "Telemetry event accepted for processing." });
        }
        catch (DependencyCircuitOpenException ex)
        {
            return ServiceUnavailable(ex);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("batch")]
    [AuthorizeWhenEnabled]
    public async Task<IActionResult> IngestBatch(
        [FromBody] TelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ingestBatchUseCase.ExecuteAsync(request, cancellationToken);
            return Accepted(new { message = "Telemetry batch accepted for processing." });
        }
        catch (DependencyCircuitOpenException ex)
        {
            return ServiceUnavailable(ex);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private ObjectResult ServiceUnavailable(DependencyCircuitOpenException ex)
    {
        if (ex.RetryAfter is { } retryAfter)
            Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            error = ex.Message,
            dependency = ex.Dependency,
            circuitBreaker = "open"
        });
    }
}
