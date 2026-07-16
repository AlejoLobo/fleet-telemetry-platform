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
        var onlineThreshold = now.AddMinutes(-_queryLimits.OnlineThresholdMinutes);
        var lastDeviceId = cursorPayload?.LastDeviceId;
        var take = pageSize + 1;

        var pageRecords = await (
            from state in _dbContext.FleetVehicleStates.AsNoTracking()
            join device in _dbContext.FleetDevices.AsNoTracking()
                on state.DeviceId equals device.DeviceId into devices
            from device in devices.DefaultIfEmpty()
            where (lastDeviceId == null || state.DeviceId.CompareTo(lastDeviceId.Value) > 0)
                && (!liveOnly || state.LastTimestamp >= onlineThreshold)
                && (!excludeSimulated || state.LocationSource != "simulated")
            orderby state.DeviceId
            select new FleetStatusJoinRow(
                state,
                device != null ? device.VehicleName : null,
                device != null ? device.VehicleType : null)
        )
            .Take(take)
            .ToListAsync(cancellationToken);

        var hasMore = pageRecords.Count > pageSize;
        var page = hasMore ? pageRecords.Take(pageSize).ToList() : pageRecords;

        var items = page
            .Select(row => MapToStatus(row.State, row.VehicleName, row.VehicleType, headingDegrees: null, now))
            .ToList();

        string? nextCursor = null;
        if (hasMore && page.Count > 0)
        {
            var last = page[^1];
            nextCursor = CursorCodec.Encode(new FleetCursorPayload(
                FleetCursorPayload.CurrentVersion,
                last.State.DeviceId,
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
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var row = await (
            from state in _dbContext.FleetVehicleStates.AsNoTracking()
            join device in _dbContext.FleetDevices.AsNoTracking()
                on state.DeviceId equals device.DeviceId into devices
            from device in devices.DefaultIfEmpty()
            where state.DeviceId == deviceId
            select new FleetStatusJoinRow(
                state,
                device != null ? device.VehicleName : null,
                device != null ? device.VehicleType : null)
        ).SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var previous = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DeviceId == deviceId && e.Timestamp < row.State.LastTimestamp)
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.EventId)
            .Take(1)
            .SingleOrDefaultAsync(cancellationToken);

        var heading = previous is not null
            ? GeoBearing.ComputeHeadingDegrees(previous, ToTelemetryPoint(row.State))
            : null;

        return MapToStatus(row.State, row.VehicleName, row.VehicleType, heading, _timeProvider.GetUtcNow());
    }

    private VehicleLatestStatusResponse MapToStatus(
        FleetVehicleStateRecord record,
        string? vehicleName,
        string? vehicleType,
        double? headingDegrees,
        DateTimeOffset now)
    {
        var connectivityStatus = VehicleConnectivityStatus.Resolve(
            record.LastTimestamp,
            now,
            _queryLimits.OnlineThresholdMinutes);

        return new VehicleLatestStatusResponse(
            DeviceId: record.DeviceId,
            VehicleName: string.IsNullOrWhiteSpace(vehicleName)
                ? record.DeviceId.ToString("D")
                : vehicleName,
            VehicleType: Domain.ValueObjects.VehicleType.ParseOrDefault(vehicleType).Value,
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

    private sealed record FleetStatusJoinRow(
        FleetVehicleStateRecord State,
        string? VehicleName,
        string? VehicleType);
}
