using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Api.Filters;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
    private readonly SseOptions _sseOptions;

    public EventsController(FleetSseBroker broker, IOptions<SseOptions> sseOptions)
    {
        _broker = broker;
        _sseOptions = sseOptions.Value;
    }

    [HttpGet("stream")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var lastEventId = ParseLastEventId(
            Request.Headers["Last-Event-ID"].FirstOrDefault()
            ?? Request.Query["lastEventId"].FirstOrDefault());
        var reader = _broker.Subscribe(out var subscriptionId);

        try
        {
            var connected = _broker.Publish(
                FleetRealtimeEventTypes.Connected,
                new { status = "connected", mode = _sseOptions.Mode.ToString() });
            await WriteSseEventAsync(connected, cancellationToken);

            foreach (var replayEvent in _broker.GetReplayAfter(lastEventId, _sseOptions.ReplayBufferSize))
            {
                await WriteSseEventAsync(replayEvent, cancellationToken);
            }

            await foreach (var sseEvent in reader.ReadAllAsync(cancellationToken))
            {
                await WriteSseEventAsync(sseEvent, cancellationToken);
            }
        }
        finally
        {
            _broker.Unsubscribe(subscriptionId);
        }
    }

    private async Task WriteSseEventAsync(FleetSseEvent sseEvent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(sseEvent.Data, JsonOptions);
        await Response.WriteAsync($"id: {sseEvent.StreamId}\n", cancellationToken);
        await Response.WriteAsync($"event: {sseEvent.EventType}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static long ParseLastEventId(string? raw) =>
        long.TryParse(raw, out var parsed) && parsed >= 0 ? parsed : 0;
}
