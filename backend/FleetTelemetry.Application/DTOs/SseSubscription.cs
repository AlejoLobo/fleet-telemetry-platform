using System.Threading.Channels;
using FleetTelemetry.Application.Realtime;

namespace FleetTelemetry.Application.DTOs;

// Vista atómica replay + live para una conexión SSE.
public sealed class SseSubscription
{
    public required Guid SubscriptionId { get; init; }

    public required SseReplayStatus ReplayStatus { get; init; }

    public required IReadOnlyList<FleetSseEvent> ReplayEvents { get; init; }

    public required ChannelReader<FleetSseEvent> LiveReader { get; init; }

    public required long CutoverId { get; init; }

    public long? LatestEventId { get; init; }

    public string? ResetReason { get; init; }
}
