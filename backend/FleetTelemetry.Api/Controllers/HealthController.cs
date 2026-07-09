using FleetTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
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
