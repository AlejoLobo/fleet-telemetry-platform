using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Repositories;

/// <summary>
/// </summary>
public class TimescaleAnalyticsQueryService : IAnalyticsQueryService
{
    private readonly FleetDbContext _dbContext;
    private readonly ILogger<TimescaleAnalyticsQueryService> _logger;

    public TimescaleAnalyticsQueryService(FleetDbContext dbContext, ILogger<TimescaleAnalyticsQueryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<double> GetAverageSpeedAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var speeds = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DeviceId == deviceId && e.Timestamp >= from && e.Timestamp <= to)
            .Select(e => e.SpeedKmh)
            .ToListAsync(cancellationToken);

        if (speeds.Count == 0)
        {
            _logger.LogDebug("Sin telemetría para {DeviceId} en el rango solicitado", deviceId);
            return 0;
        }

        return speeds.Average();
    }
}
