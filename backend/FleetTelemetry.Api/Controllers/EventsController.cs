using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Api.Filters;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

// Controlador de eventos SSE en tiempo real.
namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FleetSseBroker _broker;
    private readonly AuthOptions _authOptions;

    public EventsController(FleetSseBroker broker, IOptions<AuthOptions> authOptions)
    {
        _broker = broker;
        _authOptions = authOptions.Value;
    }

    // Emite ticket efímero para EventSource; requiere JWT en Authorization.
    [HttpPost("stream/ticket")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public ActionResult<SseStreamTicketResponse> IssueStreamTicket()
    {
        if (!_authOptions.Enabled)
            return BadRequest(new { error = "Autenticación deshabilitada; no se requiere ticket SSE." });

        var ticketService = HttpContext.RequestServices.GetRequiredService<ISseStreamTicketService>();
        var ticket = ticketService.IssueTicket(User);
        return Ok(new SseStreamTicketResponse(ticket, _authOptions.SseTicketLifetimeSeconds));
    }

    // Mantiene conexión SSE y reenvía eventos del broker.
    [HttpGet("stream")]
    [SseStreamAuthorize]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var reader = _broker.Subscribe(out var subscriptionId);

        try
        {
            await WriteEventAsync("connected", new { status = "connected" }, cancellationToken);

            await foreach (var sseEvent in reader.ReadAllAsync(cancellationToken))
            {
                await WriteEventAsync(sseEvent.EventType, sseEvent.Data, cancellationToken);
            }
        }
        finally
        {
            _broker.Unsubscribe(subscriptionId);
        }
    }

    private async Task WriteEventAsync(string eventType, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await Response.WriteAsync($"event: {eventType}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
