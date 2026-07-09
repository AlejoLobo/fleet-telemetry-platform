using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Repositories;

public class TimescaleIdempotencyStore : IIdempotencyStore
{
    private readonly FleetDbContext _dbContext;
    private readonly ILogger<TimescaleIdempotencyStore> _logger;

    public TimescaleIdempotencyStore(FleetDbContext dbContext, ILogger<TimescaleIdempotencyStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var processedAt = DateTimeOffset.UtcNow;

        var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO processed_events ("EventId", "ProcessedAt")
            VALUES ({eventId}, {processedAt})
            ON CONFLICT ("EventId") DO NOTHING
            """,
            cancellationToken);

        if (rowsAffected == 0)
        {
            _logger.LogDebug("EventId {EventId} already processed (idempotency)", eventId);
            return false;
        }

        return true;
    }
}
