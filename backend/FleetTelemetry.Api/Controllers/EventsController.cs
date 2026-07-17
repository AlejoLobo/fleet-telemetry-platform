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
    private readonly IRealtimeStreamCoordinator _streamCoordinator;

    public EventsController(
        FleetSseBroker broker,
        IOptions<SseOptions> sseOptions,
        IRealtimeStreamCoordinator streamCoordinator)
    {
        _broker = broker;
        _sseOptions = sseOptions.Value;
        _streamCoordinator = streamCoordinator;
    }

    [HttpGet("stream")]
    [AuthorizeWhenEnabled(AuthorizationPolicies.FleetRead)]
    public async Task Stream(CancellationToken cancellationToken)
    {
        SseSubscription subscription;
        if (_sseOptions.Mode == SseDeliveryMode.KafkaPush)
        {
            var lastEventId = SseLastEventId.Parse(Request.Headers["Last-Event-ID"].FirstOrDefault());
            var admission = _streamCoordinator.TryOpenStream(lastEventId);
            if (!admission.Admitted || admission.Subscription is null)
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await Response.WriteAsJsonAsync(
                    new
                    {
                        status = "not_ready",
                        reason = admission.Reason ?? "kafka-push-not-ready",
                        state = admission.State.ToString()
                    },
                    cancellationToken);
                return;
            }

            subscription = admission.Subscription;
        }
        else
        {
            var lastEventId = SseLastEventId.Parse(Request.Headers["Last-Event-ID"].FirstOrDefault());
            subscription = _broker.SubscribeFrom(lastEventId);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            HttpContext.RequestAborted);
        var streamCancellation = linkedCts.Token;

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            await WriteEphemeralEventAsync(
                FleetRealtimeEventTypes.Connected,
                new { status = "connected", mode = _sseOptions.Mode.ToString() },
                streamCancellation);

            if (subscription.ReplayStatus != SseReplayStatus.ReplayAvailable)
            {
                await WriteStreamResetAsync(subscription, streamCancellation);
            }
            else
            {
                foreach (var replayEvent in subscription.ReplayEvents)
                    await WriteSseEventAsync(replayEvent, streamCancellation);
            }

            await foreach (var sseEvent in subscription.LiveReader.ReadAllAsync(streamCancellation))
            {
                if (sseEvent.StreamId >= 0 && sseEvent.StreamId <= subscription.CutoverId)
                    continue;

                if (sseEvent.StreamId < 0)
                    await WriteEphemeralEventAsync(sseEvent.EventType, sseEvent.Data, streamCancellation);
                else
                    await WriteSseEventAsync(sseEvent, streamCancellation);
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
}
