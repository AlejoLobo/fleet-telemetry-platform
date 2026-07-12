using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FleetTelemetry.Infrastructure.Configuration;

namespace FleetTelemetry.Infrastructure.Repositories;

// Agregados SQL sobre fleet_vehicle_state y alertas abiertas.
public class TimescaleFleetStateAggregateRepository : IFleetStateAggregateRepository
{
    private readonly FleetDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly QueryLimitsOptions _queryLimits;

    public TimescaleFleetStateAggregateRepository(
        FleetDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<QueryLimitsOptions> queryLimits)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _queryLimits = queryLimits.Value;
    }

    public async Task<FleetAggregateSnapshot> GetFleetAggregateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var threshold = _timeProvider.GetUtcNow().AddMinutes(-_queryLimits.OnlineThresholdMinutes);

        var row = await _dbContext.Database
            .SqlQuery<FleetAggregateRow>($"""
                SELECT
                    COUNT(*)::int AS "TotalVehicles",
                    COUNT(*) FILTER (WHERE "LastTimestamp" >= {threshold})::int AS "ActiveVehicles",
                    MAX("LastTimestamp") AS "LastTelemetryAt"
                FROM fleet_vehicle_state
                """)
            .SingleAsync(cancellationToken);

        return new FleetAggregateSnapshot(
            row.TotalVehicles,
            row.ActiveVehicles,
            row.LastTelemetryAt);
    }

    public async Task<int> CountOpenCriticalAlertsAsync(CancellationToken cancellationToken = default)
    {
        var count = await _dbContext.Database
            .SqlQuery<CountRow>($"""
                SELECT COUNT(*)::int AS "Count"
                FROM fleet_alerts
                WHERE "IsAcknowledged" = FALSE
                  AND LOWER("Severity") = 'critical'
                """)
            .SingleAsync(cancellationToken);

        return count.Count;
    }

    private sealed record FleetAggregateRow(int TotalVehicles, int ActiveVehicles, DateTimeOffset? LastTelemetryAt);

    private sealed record CountRow(int Count);
}
