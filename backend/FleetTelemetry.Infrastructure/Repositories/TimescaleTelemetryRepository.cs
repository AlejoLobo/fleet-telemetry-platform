using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Repositories;

public class TimescaleTelemetryRepository : ITelemetryRepository
{
    private readonly FleetDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly QueryLimitsOptions _queryLimits;

    public TimescaleTelemetryRepository(
        FleetDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<QueryLimitsOptions> queryLimits)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _queryLimits = queryLimits.Value;
    }

    public async Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        var record = new TelemetryEventRecord
        {
            EventId = telemetryEvent.EventId,
            DeviceId = telemetryEvent.DeviceId,
            DriverId = telemetryEvent.DriverId,
            Timestamp = telemetryEvent.Timestamp,
            Latitude = telemetryEvent.Latitude,
            Longitude = telemetryEvent.Longitude,
            SpeedKmh = telemetryEvent.SpeedKmh,
            FuelLevelPercent = telemetryEvent.FuelLevelPercent,
            BatteryPercent = telemetryEvent.BatteryPercent,
            LocationSource = telemetryEvent.LocationSource,
            CapturedAt = DateTimeOffset.UtcNow
        };

        _dbContext.TelemetryEvents.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CursorPage<TelemetryEvent>> GetVehicleHistoryPageAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        ValidateHistoryQuery(deviceId, from, to, pageSize);

        TelemetryHistoryCursorPayload? cursorPayload = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            cursorPayload = CursorCodec.Decode<TelemetryHistoryCursorPayload>(cursor);
            CursorValidators.ValidateHistoryCursor(
                cursorPayload,
                deviceId,
                from,
                to,
                _queryLimits.HistoryMaxRangeDays);
        }

        var take = pageSize + 1;
        var query = _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DeviceId == deviceId && e.Timestamp >= from && e.Timestamp <= to);

        if (cursorPayload?.LastTimestamp is DateTimeOffset cursorTimestamp
            && cursorPayload.LastEventId is Guid cursorEventId)
        {
            query = query.Where(e =>
                e.Timestamp < cursorTimestamp
                || (e.Timestamp == cursorTimestamp && e.EventId.CompareTo(cursorEventId) < 0));
        }

        var records = await query
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.EventId)
            .Take(take)
            .ToListAsync(cancellationToken);

        var hasMore = records.Count > pageSize;
        var pageRecords = hasMore ? records.Take(pageSize).ToList() : records;
        var items = pageRecords.Select(MapToDomain).ToList();

        string? nextCursor = null;
        if (hasMore && pageRecords.Count > 0)
        {
            var last = pageRecords[^1];
            nextCursor = CursorCodec.Encode(new TelemetryHistoryCursorPayload(
                TelemetryHistoryCursorPayload.CurrentVersion,
                deviceId,
                from,
                to,
                last.Timestamp,
                last.EventId));
        }

        return new CursorPage<TelemetryEvent>(items, nextCursor, hasMore);
    }

    private void ValidateHistoryQuery(Guid deviceId, DateTimeOffset from, DateTimeOffset to, int pageSize)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("deviceId es obligatorio.", nameof(deviceId));

        if (from >= to)
            throw new ArgumentOutOfRangeException(nameof(from), "from debe ser anterior a to.");

        var maxRange = TimeSpan.FromDays(_queryLimits.HistoryMaxRangeDays);
        if (to - from > maxRange)
            throw new ArgumentOutOfRangeException(nameof(to), $"El rango no puede superar {_queryLimits.HistoryMaxRangeDays} días.");

        if (pageSize < 1 || pageSize > _queryLimits.HistoryMaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"pageSize debe estar entre 1 y {_queryLimits.HistoryMaxPageSize}.");
    }

    private static TelemetryEvent MapToDomain(TelemetryEventRecord record) =>
        TelemetryEvent.FromPersistence(
            record.EventId,
            record.DeviceId,
            record.DriverId,
            record.Timestamp,
            record.Latitude,
            record.Longitude,
            record.SpeedKmh,
            record.FuelLevelPercent,
            record.BatteryPercent,
            record.LocationSource);
}
