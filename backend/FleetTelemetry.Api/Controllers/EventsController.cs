using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.AspNetCore.Mvc;
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

    public EventsController(FleetSseBroker broker)
    {
        _broker = broker;
    }

    // Mantiene conexión SSE y reenvía eventos del broker.
    [HttpGet("stream")]
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
