using FleetTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new
        {
            status = "alive",
            service = "fleet-telemetry-api",
            timestamp = DateTimeOffset.UtcNow
        });
    }

    // Readiness: TimescaleDB + Kafka metadata (sin secretos).
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(
        [FromServices] IReadinessCheckService readinessCheckService,
        CancellationToken cancellationToken)
    {
        var result = await readinessCheckService.CheckAsync(cancellationToken);
        if (result.Status != "ready")
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);

        return Ok(result);
    }

    [HttpGet]
    public IActionResult Get([FromServices] ICircuitBreakerStateRegistry registry)
    {
        var circuits = registry.GetSnapshots();
        var openCircuits = circuits.Where(c => c.State == CircuitBreakerState.Open.ToString()).ToList();
        var status = openCircuits.Count > 0 ? "degraded" : "healthy";

        return Ok(new
        {
            status,
            service = "FleetTelemetry.Api",
            circuitBreakers = circuits,
            openCircuits = openCircuits.Select(c => c.Dependency).ToList()
        });
    }

    [HttpGet("circuit-breakers")]
    public IActionResult CircuitBreakers([FromServices] ICircuitBreakerStateRegistry registry)
    {
        return Ok(new
        {
            circuitBreakers = registry.GetSnapshots(),
            anyOpen = registry.GetSnapshots().Any(c => c.State == CircuitBreakerState.Open.ToString())
        });
    }
}
