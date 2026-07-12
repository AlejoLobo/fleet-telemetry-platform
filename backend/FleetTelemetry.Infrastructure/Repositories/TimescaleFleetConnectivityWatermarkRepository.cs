using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetTelemetry.Infrastructure.Repositories;

public sealed class TimescaleFleetConnectivityWatermarkRepository : IFleetConnectivityWatermarkRepository
{
    private readonly FleetDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public TimescaleFleetConnectivityWatermarkRepository(
        FleetDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<DateTimeOffset?> GetPreviousOnlineThresholdAsync(CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FleetConnectivityWatermarks
            .AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == 1, cancellationToken);

        return record?.PreviousOnlineThreshold;
    }

    public async Task SetPreviousOnlineThresholdAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var rows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO fleet_connectivity_watermark ("Id", "PreviousOnlineThreshold", "UpdatedAt")
            VALUES (1, {threshold}, {now})
            ON CONFLICT ("Id") DO UPDATE
            SET "PreviousOnlineThreshold" = EXCLUDED."PreviousOnlineThreshold",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            cancellationToken);

        if (rows == 0)
            throw new InvalidOperationException("No se pudo persistir el watermark de conectividad.");
    }
}
