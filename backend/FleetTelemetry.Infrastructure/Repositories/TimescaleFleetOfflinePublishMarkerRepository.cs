using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetTelemetry.Infrastructure.Repositories;

public sealed class TimescaleFleetOfflinePublishMarkerRepository : IFleetOfflinePublishMarkerRepository
{
    private readonly FleetDbContext _dbContext;

    public TimescaleFleetOfflinePublishMarkerRepository(FleetDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ShouldPublishOfflineAsync(
        string vehicleId,
        Guid lastEventId,
        CancellationToken cancellationToken = default)
    {
        var marker = await _dbContext.FleetOfflinePublishMarkers
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.VehicleId == vehicleId, cancellationToken);

        return marker is null || marker.LastEventId != lastEventId;
    }

    public async Task MarkOfflinePublishedAsync(
        string vehicleId,
        Guid lastEventId,
        DateTimeOffset statusEvaluatedAt,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO fleet_offline_publish_markers ("VehicleId", "LastEventId", "StatusEvaluatedAt")
            VALUES ({vehicleId}, {lastEventId}, {statusEvaluatedAt})
            ON CONFLICT ("VehicleId") DO UPDATE
            SET "LastEventId" = EXCLUDED."LastEventId",
                "StatusEvaluatedAt" = EXCLUDED."StatusEvaluatedAt"
            """,
            cancellationToken);
    }

    public async Task MarkOnlineAsync(string vehicleId, CancellationToken cancellationToken = default)
    {
        await _dbContext.FleetOfflinePublishMarkers
            .Where(m => m.VehicleId == vehicleId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
