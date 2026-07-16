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
        var marker = await _dbContext.FleetOfflinePublishMarkers
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.DeviceId == deviceId, cancellationToken);

        return marker is null || marker.LastEventId != lastEventId;
    }

    public async Task MarkOfflinePublishedAsync(
        Guid deviceId,
        Guid lastEventId,
        DateTimeOffset statusEvaluatedAt,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO fleet_offline_publish_markers (device_id, "LastEventId", "StatusEvaluatedAt")
            VALUES ({deviceId}, {lastEventId}, {statusEvaluatedAt})
            ON CONFLICT (device_id) DO UPDATE
            SET "LastEventId" = EXCLUDED."LastEventId",
                "StatusEvaluatedAt" = EXCLUDED."StatusEvaluatedAt"
            """,
            cancellationToken);
    }

    public async Task MarkOnlineAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        await _dbContext.FleetOfflinePublishMarkers
            .Where(m => m.DeviceId == deviceId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
