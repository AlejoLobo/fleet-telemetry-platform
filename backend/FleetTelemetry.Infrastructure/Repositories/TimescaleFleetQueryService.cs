using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Repositories;

public class TimescaleFleetQueryService : IFleetQueryService
{
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromMinutes(5);

    private readonly FleetDbContext _dbContext;
    private readonly ILogger<TimescaleFleetQueryService> _logger;

    public TimescaleFleetQueryService(FleetDbContext dbContext, ILogger<TimescaleFleetQueryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VehicleLatestStatusResponse>> GetLatestVehicleStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        var latest = records
            .GroupBy(e => e.VehicleId)
            .Select(g => g.First())
            .Select(MapToStatus)
            .OrderBy(v => v.VehicleId)
            .ToList();

        _logger.LogDebug("Consultados {Count} vehículos con telemetría", latest.Count);
        return latest;
    }

    public async Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
        string vehicleId,
        CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicleId)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        return record is null ? null : MapToStatus(record);
    }

    private static VehicleLatestStatusResponse MapToStatus(TelemetryEventRecord record)
    {
        var isOnline = DateTimeOffset.UtcNow - record.Timestamp <= OnlineThreshold;

        return new VehicleLatestStatusResponse(
            VehicleId: record.VehicleId,
            Name: record.VehicleId,
            Status: isOnline ? "online" : "offline",
            LastSeenAt: record.Timestamp,
            LastSpeedKmh: record.SpeedKmh,
            LastLatitude: record.Latitude,
            LastLongitude: record.Longitude);
    }
}
