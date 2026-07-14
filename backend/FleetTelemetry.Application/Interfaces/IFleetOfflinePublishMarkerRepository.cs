namespace FleetTelemetry.Application.Interfaces;

public interface IFleetOfflinePublishMarkerRepository
{
    Task<bool> ShouldPublishOfflineAsync(
        string vehicleId,
        Guid lastEventId,
        CancellationToken cancellationToken = default);

    Task MarkOfflinePublishedAsync(
        string vehicleId,
        Guid lastEventId,
        DateTimeOffset statusEvaluatedAt,
        CancellationToken cancellationToken = default);

    Task MarkOnlineAsync(string vehicleId, CancellationToken cancellationToken = default);
}
