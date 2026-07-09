using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Geo;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Repositories;

public class TimescaleFleetQueryService : IFleetQueryService
{
    /// <summary>Ventana para considerar un vehículo "en línea" en el dashboard.</summary>
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromMinutes(5);

    private readonly FleetDbContext _dbContext;
    private readonly ILogger<TimescaleFleetQueryService> _logger;

    public TimescaleFleetQueryService(FleetDbContext dbContext, ILogger<TimescaleFleetQueryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VehicleLatestStatusResponse>> GetLatestVehicleStatusesAsync(
        bool liveOnly = false,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        var latest = records
            .GroupBy(e => e.VehicleId)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(e => e.Timestamp).Take(2).ToList();
                var current = ordered[0];
                var previous = ordered.Count > 1 ? ordered[1] : null;
                var heading = GeoBearing.ComputeHeadingDegrees(previous, current);
                return MapToStatus(current, heading);
            })
            .OrderBy(v => v.VehicleId)
            .ToList();

        if (liveOnly)
            latest = latest.Where(v => v.Status == "online").ToList();

        _logger.LogDebug("Consultados {Count} vehículos con telemetría (liveOnly={LiveOnly})", latest.Count, liveOnly);
        return latest;
    }

    public async Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
        string vehicleId,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicleId)
            .OrderByDescending(e => e.Timestamp)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (records.Count == 0) return null;

        var heading = records.Count > 1
            ? GeoBearing.ComputeHeadingDegrees(records[1], records[0])
            : null;

        return MapToStatus(records[0], heading);
    }

    private static VehicleLatestStatusResponse MapToStatus(
        TelemetryEventRecord record,
        double? headingDegrees)
    {
        var isOnline = DateTimeOffset.UtcNow - record.Timestamp <= OnlineThreshold;

        return new VehicleLatestStatusResponse(
            VehicleId: record.VehicleId,
            Name: record.VehicleId,
            Status: isOnline ? "online" : "offline",
            LastSeenAt: record.Timestamp,
            LastSpeedKmh: record.SpeedKmh,
            LastLatitude: record.Latitude,
            LastLongitude: record.Longitude,
            LastHeadingDegrees: headingDegrees);
    }
}
