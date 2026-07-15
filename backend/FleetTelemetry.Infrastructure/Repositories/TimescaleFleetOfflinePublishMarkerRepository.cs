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
        Guid deviceId,
        Guid lastEventId,
        CancellationToken cancellationToken = default)
    {
        var deviceIdStorage = deviceId.ToString("D");
        var marker = await _dbContext.FleetOfflinePublishMarkers
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.VehicleId == deviceIdStorage, cancellationToken);

        return marker is null || marker.LastEventId != lastEventId;
    }

    public async Task MarkOfflinePublishedAsync(
        Guid deviceId,
        Guid lastEventId,
        DateTimeOffset statusEvaluatedAt,
        CancellationToken cancellationToken = default)
    {
        var deviceIdStorage = deviceId.ToString("D");
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO fleet_offline_publish_markers ("VehicleId", "LastEventId", "StatusEvaluatedAt")
            VALUES ({deviceIdStorage}, {lastEventId}, {statusEvaluatedAt})
            ON CONFLICT ("VehicleId") DO UPDATE
            SET "LastEventId" = EXCLUDED."LastEventId",
                "StatusEvaluatedAt" = EXCLUDED."StatusEvaluatedAt"
            """,
            cancellationToken);
    }

    public async Task MarkOnlineAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var deviceIdStorage = deviceId.ToString("D");
        await _dbContext.FleetOfflinePublishMarkers
            .Where(m => m.VehicleId == deviceIdStorage)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
