using System.Collections.Concurrent;

namespace FleetTelemetry.Infrastructure.Realtime;

// Evita republicar offline para el mismo LastEventId.
public sealed class FleetConnectivityPublishTracker
{
    private readonly ConcurrentDictionary<string, Guid> _offlinePublishedByEventId = new();

    public bool ShouldPublishOffline(string vehicleId, Guid lastEventId) =>
        !_offlinePublishedByEventId.TryGetValue(vehicleId, out var published) || published != lastEventId;

    public void MarkOfflinePublished(string vehicleId, Guid lastEventId) =>
        _offlinePublishedByEventId[vehicleId] = lastEventId;

    public void MarkOnline(string vehicleId) =>
        _offlinePublishedByEventId.TryRemove(vehicleId, out _);
}
