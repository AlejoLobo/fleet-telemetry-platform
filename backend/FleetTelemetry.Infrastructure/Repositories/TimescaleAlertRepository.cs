using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetTelemetry.Infrastructure.Repositories;

public class TimescaleAlertRepository : IAlertRepository
{
    private readonly FleetDbContext _dbContext;

    public TimescaleAlertRepository(FleetDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.FleetAlerts
            .AsNoTracking()
            .Where(a => !a.IsAcknowledged)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAfterCursorAsync(
        AlertStreamCursor cursor,
        DateTimeOffset upperBound,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.FleetAlerts
            .AsNoTracking()
            .Where(a => !a.IsAcknowledged && a.CreatedAt <= upperBound)
            .Where(a =>
                a.CreatedAt > cursor.CreatedAt
                || (a.CreatedAt == cursor.CreatedAt && a.AlertId.CompareTo(cursor.AlertId) > 0))
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.AlertId)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken);

        return records.Select(MapToDomain).ToList();
    }

    public async Task<bool> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FleetAlerts
            .FirstOrDefaultAsync(a => a.AlertId == alertId, cancellationToken);

        if (record is null || record.IsAcknowledged)
            return false;

        record.IsAcknowledged = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default)
    {
        var record = new FleetAlertRecord
        {
            AlertId = alert.AlertId,
            VehicleId = alert.DeviceIdStorage,
            AlertType = alert.AlertType,
            Severity = alert.Severity,
            Message = alert.Message,
            CreatedAt = alert.CreatedAt,
            IsAcknowledged = alert.IsAcknowledged
        };

        _dbContext.FleetAlerts.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FleetAlert MapToDomain(FleetAlertRecord record) =>
        FleetAlert.FromPersistence(
            record.AlertId,
            record.VehicleId,
            record.AlertType,
            record.Severity,
            record.Message,
            record.CreatedAt,
            record.IsAcknowledged);
}
