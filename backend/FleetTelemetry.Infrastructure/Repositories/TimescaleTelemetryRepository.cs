using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using FleetTelemetry.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

// Repositorio de telemetría en TimescaleDB.
namespace FleetTelemetry.Infrastructure.Repositories;

// Lectura y escritura de eventos históricos.
public class TimescaleTelemetryRepository : ITelemetryRepository
{
    private readonly FleetDbContext _dbContext;

    public TimescaleTelemetryRepository(FleetDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        var record = new TelemetryEventRecord
        {
            EventId = telemetryEvent.EventId,
            VehicleId = telemetryEvent.VehicleId,
            DriverId = telemetryEvent.DriverId,
            Timestamp = telemetryEvent.Timestamp,
            Latitude = telemetryEvent.Latitude,
            Longitude = telemetryEvent.Longitude,
            SpeedKmh = telemetryEvent.SpeedKmh,
            FuelLevelPercent = telemetryEvent.FuelLevelPercent,
            BatteryPercent = telemetryEvent.BatteryPercent,
            CapturedAt = DateTimeOffset.UtcNow
        };

        _dbContext.TelemetryEvents.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TelemetryEvent>> GetByVehicleAsync(
        string vehicleId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicleId && e.Timestamp >= from && e.Timestamp <= to)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        return records.Select(MapToDomain).ToList();
    }

    private static TelemetryEvent MapToDomain(TelemetryEventRecord record) =>
        TelemetryEvent.FromPersistence(
            record.EventId,
            record.VehicleId,
            record.DriverId,
            record.Timestamp,
            record.Latitude,
            record.Longitude,
            record.SpeedKmh,
            record.FuelLevelPercent,
            record.BatteryPercent);
}
