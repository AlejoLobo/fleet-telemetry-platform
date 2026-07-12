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

// Detecta vehículos recién expirados y publica vehicle-update offline.
public sealed class FleetConnectivityExpiryService : IFleetConnectivityExpiryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FleetDbContext _dbContext;
    private readonly IFleetRealtimePublisher _realtimePublisher;
    private readonly FleetConnectivityPublishTracker _publishTracker;
    private readonly FleetConnectivityExpiryState _expiryState;
    private readonly TimeProvider _timeProvider;
    private readonly QueryLimitsOptions _queryLimits;
    private readonly SseOptions _sseOptions;
    private readonly ILogger<FleetConnectivityExpiryService> _logger;

    public FleetConnectivityExpiryService(
        FleetDbContext dbContext,
        IFleetRealtimePublisher realtimePublisher,
        FleetConnectivityPublishTracker publishTracker,
        FleetConnectivityExpiryState expiryState,
        TimeProvider timeProvider,
        IOptions<QueryLimitsOptions> queryLimits,
        IOptions<SseOptions> sseOptions,
        ILogger<FleetConnectivityExpiryService> logger)
    {
        _dbContext = dbContext;
        _realtimePublisher = realtimePublisher;
        _publishTracker = publishTracker;
        _expiryState = expiryState;
        _timeProvider = timeProvider;
        _queryLimits = queryLimits.Value;
        _sseOptions = sseOptions.Value;
        _logger = logger;
    }

    public async Task<int> PublishOfflineTransitionsAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var currentThreshold = now.AddMinutes(-_queryLimits.OnlineThresholdMinutes);
        var previousThreshold = _expiryState.PreviousOnlineThreshold
            ?? currentThreshold.Subtract(TimeSpan.FromSeconds(_sseOptions.ConnectivityExpiryLookbackSeconds));
        _expiryState.PreviousOnlineThreshold = currentThreshold;

        var expiredStates = await _dbContext.FleetVehicleStates
            .AsNoTracking()
            .Where(state =>
                state.LastTimestamp < currentThreshold
                && state.LastTimestamp >= previousThreshold)
            .OrderBy(state => state.LastTimestamp)
            .Take(_sseOptions.ConnectivityExpiryBatchSize)
            .ToListAsync(cancellationToken);

        var published = 0;

        foreach (var state in expiredStates)
        {
            if (!_publishTracker.ShouldPublishOffline(state.VehicleId, state.LastEventId))
                continue;

            var payload = BuildOfflinePayload(state, now);
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

            await _realtimePublisher.PublishVehicleUpdateAsync(
                state.VehicleId,
                payloadJson,
                cancellationToken);

            _publishTracker.MarkOfflinePublished(state.VehicleId, state.LastEventId);
            published += 1;

            _logger.LogDebug(
                "Published offline transition for vehicle {VehicleId} (LastEventId={LastEventId})",
                state.VehicleId,
                state.LastEventId);
        }

        return published;
    }

    private static VehicleLatestStatusResponse BuildOfflinePayload(
        FleetVehicleStateRecord state,
        DateTimeOffset now)
    {
        _ = now;
        return new VehicleLatestStatusResponse(
            state.VehicleId,
            state.VehicleId,
            VehicleConnectivityStatus.Offline,
            state.LastTimestamp,
            state.SpeedKmh,
            state.Latitude,
            state.Longitude,
            null,
            state.LocationSource,
            state.LastEventId);
    }
}
