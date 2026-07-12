namespace FleetTelemetry.Application.Interfaces;

// Agregados SQL sobre fleet_vehicle_state y alertas.
public interface IFleetStateAggregateRepository
{
    Task<FleetAggregateSnapshot> GetFleetAggregateSnapshotAsync(CancellationToken cancellationToken = default);

    Task<int> CountOpenCriticalAlertsAsync(CancellationToken cancellationToken = default);
}

public sealed record FleetAggregateSnapshot(
    int TotalVehicles,
    int ActiveVehicles,
    DateTimeOffset? LastTelemetryAt);
