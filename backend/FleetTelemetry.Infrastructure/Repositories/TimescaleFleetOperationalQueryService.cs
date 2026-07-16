using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Repositories;

public class TimescaleFleetOperationalQueryService : IFleetOperationalQueryService
{
    private readonly FleetDbContext _dbContext;
    private readonly StoppedVehicleQueryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TimescaleFleetOperationalQueryService> _logger;

    public TimescaleFleetOperationalQueryService(
        FleetDbContext dbContext,
        IOptions<StoppedVehicleQueryOptions> options,
        TimeProvider timeProvider,
        ILogger<TimescaleFleetOperationalQueryService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StoppedVehicleStatusDto>> GetVehiclesStoppedLongerThanAsync(
        TimeSpan minDuration,
        double stoppedSpeedThresholdKmh = 1,
        CancellationToken cancellationToken = default)
    {
        var asOf = _timeProvider.GetUtcNow();
        var lookbackHours = Math.Max(1, _options.LookbackHours);
        var freshnessMinutes = Math.Max(1, _options.VehicleFreshnessMinutes);
        var maxGapMinutes = Math.Max(1, _options.MaximumTelemetryGapMinutes);
        var minDurationMinutes = (int)Math.Ceiling(minDuration.TotalMinutes);
        var speedThreshold = stoppedSpeedThresholdKmh > 0
            ? stoppedSpeedThresholdKmh
            : _options.StoppedSpeedThresholdKmh;

        // stopped_since = primer evento detenido posterior al último movimiento dentro del lookback.
        var rows = await _dbContext.Database
            .SqlQueryRaw<StoppedVehicleRow>(
                """
                WITH recent AS (
                    SELECT device_id, "Timestamp", "SpeedKmh", "Latitude", "Longitude"
                    FROM telemetry_events
                    WHERE "Timestamp" >= {0} - make_interval(hours => {1})
                      AND COALESCE("LocationSource", 'gps') <> 'simulated'
                ),
                latest AS (
                    SELECT DISTINCT ON (device_id)
                        device_id, "Timestamp" AS last_seen_at, "SpeedKmh", "Latitude", "Longitude"
                    FROM recent
                    ORDER BY device_id, "Timestamp" DESC
                ),
                last_move AS (
                    SELECT r.device_id, MAX(r."Timestamp") AS last_moving_at
                    FROM recent r
                    WHERE r."SpeedKmh" > {2}
                    GROUP BY r.device_id
                ),
                first_stop_after_move AS (
                    SELECT DISTINCT ON (r.device_id)
                        r.device_id,
                        r."Timestamp" AS stopped_since
                    FROM recent r
                    INNER JOIN latest l ON l.device_id = r.device_id
                    LEFT JOIN last_move m ON m.device_id = r.device_id
                    WHERE r."SpeedKmh" <= {2}
                      AND r."Timestamp" > COALESCE(
                          m.last_moving_at,
                          {0} - make_interval(hours => {1}))
                      AND r."Timestamp" <= l.last_seen_at
                    ORDER BY r.device_id, r."Timestamp" ASC
                ),
                event_gaps AS (
                    SELECT
                        gaps.device_id,
                        MAX(gaps.gap) AS max_gap
                    FROM (
                        SELECT
                            r.device_id,
                            r."Timestamp" - LAG(r."Timestamp") OVER (
                                PARTITION BY r.device_id ORDER BY r."Timestamp") AS gap
                        FROM recent r
                        INNER JOIN first_stop_after_move s ON s.device_id = r.device_id
                        INNER JOIN latest l ON l.device_id = r.device_id
                        WHERE r."Timestamp" BETWEEN s.stopped_since AND l.last_seen_at
                    ) gaps
                    WHERE gaps.gap IS NOT NULL
                    GROUP BY gaps.device_id
                ),
                has_intermediate_move AS (
                    SELECT DISTINCT r.device_id
                    FROM recent r
                    INNER JOIN first_stop_after_move s ON s.device_id = r.device_id
                    INNER JOIN latest l ON l.device_id = r.device_id
                    WHERE r."Timestamp" > s.stopped_since
                      AND r."Timestamp" < l.last_seen_at
                      AND r."SpeedKmh" > {2}
                )
                SELECT
                    l.device_id AS "DeviceId",
                    l.last_seen_at AS "LastSeenAt",
                    s.stopped_since AS "StoppedSince",
                    l."Latitude",
                    l."Longitude"
                FROM latest l
                INNER JOIN first_stop_after_move s ON s.device_id = l.device_id
                LEFT JOIN event_gaps g ON g.device_id = l.device_id
                LEFT JOIN has_intermediate_move im ON im.device_id = l.device_id
                WHERE l."SpeedKmh" <= {2}
                  AND im.device_id IS NULL
                  AND l.last_seen_at >= {0} - make_interval(mins => {3})
                  AND (g.max_gap IS NULL OR g.max_gap <= make_interval(mins => {4}))
                  AND l.last_seen_at - s.stopped_since >= make_interval(mins => {5})
                ORDER BY l.device_id
                """,
                asOf,
                lookbackHours,
                speedThreshold,
                freshnessMinutes,
                maxGapMinutes,
                minDurationMinutes)
            .ToListAsync(cancellationToken);

        var result = rows.Select(row =>
        {
            var duration = row.LastSeenAt - row.StoppedSince;
            var zone = CriticalZoneCatalog.FindZoneAt(row.Latitude, row.Longitude);
            return new StoppedVehicleStatusDto(
                row.DeviceId,
                row.LastSeenAt,
                row.StoppedSince,
                duration,
                row.Latitude,
                row.Longitude,
                zone?.Name);
        }).ToList();

        _logger.LogDebug(
            "Consultados {Count} vehículos detenidos ≥ {Minutes} min (umbral {Speed} km/h, frescura {Freshness} min)",
            result.Count,
            minDurationMinutes,
            speedThreshold,
            freshnessMinutes);

        return result;
    }

    private sealed record StoppedVehicleRow(
        Guid DeviceId,
        DateTimeOffset LastSeenAt,
        DateTimeOffset StoppedSince,
        double Latitude,
        double Longitude);
}
