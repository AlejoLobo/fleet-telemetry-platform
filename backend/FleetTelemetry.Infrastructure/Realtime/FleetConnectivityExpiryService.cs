using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

public sealed class FleetConnectivityExpiryService : IFleetConnectivityExpiryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FleetDbContext _dbContext;
    private readonly IFleetRealtimePublisher _realtimePublisher;
    private readonly IFleetOfflinePublishMarkerRepository _markerRepository;
    private readonly IFleetConnectivityWatermarkRepository _watermarkRepository;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly QueryLimitsOptions _queryLimits;
    private readonly SseOptions _sseOptions;
    private readonly ILogger<FleetConnectivityExpiryService> _logger;

    public FleetConnectivityExpiryService(
        FleetDbContext dbContext,
        IFleetRealtimePublisher realtimePublisher,
        IFleetOfflinePublishMarkerRepository markerRepository,
        IFleetConnectivityWatermarkRepository watermarkRepository,
        IDeviceRegistry deviceRegistry,
        TimeProvider timeProvider,
        IOptions<QueryLimitsOptions> queryLimits,
        IOptions<SseOptions> sseOptions,
        ILogger<FleetConnectivityExpiryService> logger)
    {
        _dbContext = dbContext;
        _realtimePublisher = realtimePublisher;
        _markerRepository = markerRepository;
        _watermarkRepository = watermarkRepository;
        _deviceRegistry = deviceRegistry;
        _timeProvider = timeProvider;
        _queryLimits = queryLimits.Value;
        _sseOptions = sseOptions.Value;
        _logger = logger;
    }

    public async Task<int> PublishOfflineTransitionsAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var currentThreshold = now.AddMinutes(-_queryLimits.OnlineThresholdMinutes);
        var previousThreshold = await _watermarkRepository.GetPreviousOnlineThresholdAsync(cancellationToken)
            ?? DateTimeOffset.MinValue;

        if (previousThreshold >= currentThreshold)
            return 0;

        var pageSize = _sseOptions.ConnectivityExpiryBatchSize;
        var published = 0;
        DateTimeOffset? cursorTimestamp = null;
        Guid? cursorDeviceId = null;

        while (true)
        {
            var page = await FetchWindowPageAsync(
                previousThreshold,
                currentThreshold,
                cursorTimestamp,
                cursorDeviceId,
                pageSize,
                cancellationToken);

            if (page.Count == 0)
                break;

            foreach (var state in page)
            {
                var deviceId = state.DeviceId;
                if (!await _markerRepository.ShouldPublishOfflineAsync(
                        deviceId,
                        state.LastEventId,
                        cancellationToken))
                {
                    continue;
                }

                var payload = await BuildOfflinePayloadAsync(state, now, cancellationToken);
                var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

                await _realtimePublisher.PublishVehicleUpdateAsync(
                    deviceId,
                    payloadJson,
                    cancellationToken);

                await _markerRepository.MarkOfflinePublishedAsync(
                    deviceId,
                    state.LastEventId,
                    now,
                    cancellationToken);

                published += 1;

                _logger.LogDebug(
                    "Published offline transition for device {DeviceId} (LastEventId={LastEventId})",
                    deviceId,
                    state.LastEventId);
            }

            if (page.Count < pageSize)
                break;

            var last = page[^1];
            cursorTimestamp = last.LastTimestamp;
            cursorDeviceId = last.DeviceId;
        }

        await _watermarkRepository.SetPreviousOnlineThresholdAsync(currentThreshold, cancellationToken);
        return published;
    }

    private async Task<List<FleetVehicleStateRecord>> FetchWindowPageAsync(
        DateTimeOffset previousThreshold,
        DateTimeOffset currentThreshold,
        DateTimeOffset? cursorTimestamp,
        Guid? cursorDeviceId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.FleetVehicleStates
            .AsNoTracking()
            .Where(state =>
                state.LastTimestamp < currentThreshold
                && state.LastTimestamp >= previousThreshold);

        if (cursorTimestamp is not null && cursorDeviceId is not null)
        {
            query = query.Where(state =>
                state.LastTimestamp > cursorTimestamp.Value
                || (state.LastTimestamp == cursorTimestamp.Value
                    && state.DeviceId.CompareTo(cursorDeviceId.Value) > 0));
        }

        return await query
            .OrderBy(state => state.LastTimestamp)
            .ThenBy(state => state.DeviceId)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<VehicleLatestStatusResponse> BuildOfflinePayloadAsync(
        FleetVehicleStateRecord state,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        var device = await _deviceRegistry.GetDeviceAsync(state.DeviceId, cancellationToken);
        var vehicleName = device?.VehicleName ?? state.DeviceId.ToString("D");
        var vehicleType = device?.VehicleType ?? Domain.ValueObjects.VehicleType.Default.Value;

        return new(
            state.DeviceId,
            vehicleName,
            vehicleType,
            VehicleConnectivityStatus.Offline,
            state.LastTimestamp,
            state.SpeedKmh,
            state.Latitude,
            state.Longitude,
            null,
            state.LocationSource,
            state.LastEventId,
            evaluatedAt);
    }
}
