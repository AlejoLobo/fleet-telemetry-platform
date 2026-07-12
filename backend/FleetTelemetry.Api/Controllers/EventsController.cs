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

        var lastEventId = ParseLastEventId(Request.Headers["Last-Event-ID"].FirstOrDefault());
        var subscription = _broker.SubscribeFrom(lastEventId);

        try
        {
            await WriteEphemeralEventAsync(
                FleetRealtimeEventTypes.Connected,
                new { status = "connected", mode = _sseOptions.Mode.ToString() },
                cancellationToken);

            if (subscription.ReplayStatus != SseReplayStatus.ReplayAvailable)
            {
                await WriteStreamResetAsync(subscription, cancellationToken);
            }
            else
            {
                foreach (var replayEvent in subscription.ReplayEvents)
                    await WriteSseEventAsync(replayEvent, cancellationToken);
            }

            await foreach (var sseEvent in subscription.LiveReader.ReadAllAsync(cancellationToken))
            {
                if (sseEvent.StreamId >= 0 && sseEvent.StreamId <= subscription.CutoverId)
                    continue;

                if (sseEvent.StreamId < 0)
                    await WriteEphemeralEventAsync(sseEvent.EventType, sseEvent.Data, cancellationToken);
                else
                    await WriteSseEventAsync(sseEvent, cancellationToken);
            }
        }
        finally
        {
            _broker.Unsubscribe(subscription.SubscriptionId);
        }
    }

    private async Task WriteStreamResetAsync(SseSubscription subscription, CancellationToken cancellationToken)
    {
        var payload = new SseStreamResetData(
            subscription.ResetReason ?? "replay-gap",
            subscription.LatestEventId);

        await WriteEphemeralEventAsync(
            FleetRealtimeEventTypes.StreamReset,
            payload,
            cancellationToken);
    }

    private async Task WriteSseEventAsync(FleetSseEvent sseEvent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(sseEvent.Data, JsonOptions);
        await Response.WriteAsync($"id: {sseEvent.StreamId}\n", cancellationToken);
        await Response.WriteAsync($"event: {sseEvent.EventType}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteEphemeralEventAsync(
        string eventType,
        object data,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await Response.WriteAsync($"event: {eventType}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static long ParseLastEventId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        return long.TryParse(raw, out var parsed) && parsed >= 0 ? parsed : -1;
    }
}
