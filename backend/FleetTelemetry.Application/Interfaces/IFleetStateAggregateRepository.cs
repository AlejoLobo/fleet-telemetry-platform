namespace FleetTelemetry.Application.Interfaces;

public interface IFleetStateAggregateRepository
{
    Task<FleetAggregateSnapshot> GetFleetAggregateSnapshotAsync(CancellationToken cancellationToken = default);

    Task<int> CountOpenCriticalAlertsAsync(CancellationToken cancellationToken = default);
}

public sealed record FleetAggregateSnapshot(
    int TotalVehicles,
    int ActiveVehicles,
    DateTimeOffset? LastTelemetryAt);
