namespace FleetTelemetry.Application.Interfaces;

public interface IFleetOfflinePublishMarkerRepository
{
    Task<bool> ShouldPublishOfflineAsync(
        Guid deviceId,
        Guid lastEventId,
        CancellationToken cancellationToken = default);

    Task MarkOfflinePublishedAsync(
        Guid deviceId,
        Guid lastEventId,
        DateTimeOffset statusEvaluatedAt,
        CancellationToken cancellationToken = default);

    Task MarkOnlineAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
