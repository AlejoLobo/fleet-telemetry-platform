using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using FleetTelemetry.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FleetTelemetry.Infrastructure.Repositories;

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

    private static TelemetryEvent MapToDomain(TelemetryEventRecord record) => new()
    {
        EventId = record.EventId,
        VehicleId = record.VehicleId,
        DriverId = record.DriverId,
        Timestamp = record.Timestamp,
        Latitude = record.Latitude,
        Longitude = record.Longitude,
        SpeedKmh = record.SpeedKmh,
        FuelLevelPercent = record.FuelLevelPercent,
        BatteryPercent = record.BatteryPercent
    };
}
