using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Geo;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Repositories;

public class TimescaleFleetQueryService : IFleetQueryService
{
    private readonly FleetDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly QueryLimitsOptions _queryLimits;
    private readonly ILogger<TimescaleFleetQueryService> _logger;

    public TimescaleFleetQueryService(
        FleetDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<QueryLimitsOptions> queryLimits,
        ILogger<TimescaleFleetQueryService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _queryLimits = queryLimits.Value;
        _logger = logger;
    }

    public async Task<CursorPage<VehicleLatestStatusResponse>> GetFleetPageAsync(
        int pageSize,
        string? cursor,
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default)
    {
        ValidatePageSize(pageSize, _queryLimits.FleetMaxPageSize);

        FleetCursorPayload? cursorPayload = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            cursorPayload = CursorCodec.Decode<FleetCursorPayload>(cursor);
            CursorValidators.ValidateFleetCursor(cursorPayload, liveOnly, excludeSimulated);
        }

        var now = _timeProvider.GetUtcNow();
        var onlineThreshold = now - _queryLimits.GetOnlineWindow();
        var lastVehicleId = cursorPayload?.LastVehicleId;
        var take = pageSize + 1;

        var records = await _dbContext.FleetVehicleStates
            .AsNoTracking()
            .Where(state =>
                (lastVehicleId == null || string.Compare(state.VehicleId, lastVehicleId) > 0)
                && (!liveOnly || state.LastTimestamp >= onlineThreshold)
                && (!excludeSimulated || state.LocationSource != "simulated"))
            .OrderBy(state => state.VehicleId)
            .Take(take)
            .ToListAsync(cancellationToken);

        var hasMore = records.Count > pageSize;
        var pageRecords = hasMore ? records.Take(pageSize).ToList() : records;

        var items = pageRecords
            .Select(record => MapToStatus(record, headingDegrees: null, now))
            .ToList();

        string? nextCursor = null;
        if (hasMore && pageRecords.Count > 0)
        {
            var last = pageRecords[^1];
            nextCursor = CursorCodec.Encode(new FleetCursorPayload(
                FleetCursorPayload.CurrentVersion,
                last.VehicleId,
                liveOnly,
                excludeSimulated));
        }

        return new CursorPage<VehicleLatestStatusResponse>(items, nextCursor, hasMore);
    }

    public async Task<IReadOnlyList<VehicleLatestStatusResponse>> GetAllFleetStatusesAsync(
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default)
    {
        var all = new List<VehicleLatestStatusResponse>();
        string? cursor = null;

        while (true)
        {
            var page = await GetFleetPageAsync(
                _queryLimits.FleetMaxPageSize,
                cursor,
                liveOnly,
                excludeSimulated,
                cancellationToken);

            all.AddRange(page.Items);
            if (!page.HasMore || string.IsNullOrWhiteSpace(page.NextCursor))
                break;

            cursor = page.NextCursor;
        }

        return all;
    }

    public async Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
        string vehicleId,
        CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FleetVehicleStates
            .AsNoTracking()
            .SingleOrDefaultAsync(state => state.VehicleId == vehicleId, cancellationToken);

        if (record is null)
            return null;

        var previous = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicleId && e.Timestamp < record.LastTimestamp)
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.EventId)
            .Take(1)
            .SingleOrDefaultAsync(cancellationToken);

        var heading = previous is not null
            ? GeoBearing.ComputeHeadingDegrees(previous, ToTelemetryPoint(record))
            : null;

        return MapToStatus(record, heading, _timeProvider.GetUtcNow());
    }

    private VehicleLatestStatusResponse MapToStatus(
        FleetVehicleStateRecord record,
        double? headingDegrees,
        DateTimeOffset now)
    {
        var connectivityStatus = VehicleConnectivityStatus.Resolve(
            record.LastTimestamp,
            now,
            _queryLimits.GetOnlineWindow());

        return new VehicleLatestStatusResponse(
            VehicleId: record.VehicleId,
            Name: string.IsNullOrWhiteSpace(record.DisplayName) ? record.VehicleId : record.DisplayName,
            Status: connectivityStatus,
            LastSeenAt: record.LastTimestamp,
            LastSpeedKmh: record.SpeedKmh,
            LastLatitude: record.Latitude,
            LastLongitude: record.Longitude,
            LastHeadingDegrees: headingDegrees,
            LastLocationSource: record.LocationSource,
            LastEventId: record.LastEventId,
            StatusEvaluatedAt: now,
            DriverId: record.DriverId);
    }

    private static void ValidatePageSize(int pageSize, int maxPageSize)
    {
        if (pageSize < 1 || pageSize > maxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"pageSize debe estar entre 1 y {maxPageSize}.");
    }

    private static TelemetryEventRecord ToTelemetryPoint(FleetVehicleStateRecord record) =>
        new()
        {
            Latitude = record.Latitude,
            Longitude = record.Longitude
        };
}
