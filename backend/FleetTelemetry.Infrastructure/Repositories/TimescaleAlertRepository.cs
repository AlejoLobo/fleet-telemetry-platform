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

    public async Task<FleetAlert?> GetByIdAsync(Guid alertId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FleetAlerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AlertId == alertId, cancellationToken);

        return record is null ? null : MapToDomain(record);
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
            VehicleId = alert.VehicleId,
            AlertType = alert.AlertType,
            Severity = alert.Severity,
            Message = alert.Message,
            CreatedAt = alert.CreatedAt,
            IsAcknowledged = alert.IsAcknowledged
        };

        _dbContext.FleetAlerts.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FleetAlert MapToDomain(FleetAlertRecord record) => new()
    {
        AlertId = record.AlertId,
        VehicleId = record.VehicleId,
        AlertType = record.AlertType,
        Severity = record.Severity,
        Message = record.Message,
        CreatedAt = record.CreatedAt,
        IsAcknowledged = record.IsAcknowledged
    };
}
