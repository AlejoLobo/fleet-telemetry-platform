using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Consultas operativas de vehículos detenidos.
namespace FleetTelemetry.Infrastructure.Repositories;

// Detecta detenciones prolongadas con secuencia continua post-movimiento.
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
        var freshnessMinutes = Math.Max(1, _options.MaxFreshnessMinutes);
        var maxGapSeconds = Math.Max(30, _options.MaxTelemetryGapSeconds);
        var minDurationMinutes = (int)Math.Ceiling(minDuration.TotalMinutes);
        var speedThreshold = stoppedSpeedThresholdKmh > 0
            ? stoppedSpeedThresholdKmh
            : _options.StoppedSpeedThresholdKmh;

        // stopped_since = primer evento detenido posterior al último movimiento dentro del lookback.
        var rows = await _dbContext.Database
            .SqlQueryRaw<StoppedVehicleRow>(
                """
                WITH recent AS (
                    SELECT "VehicleId", "Timestamp", "SpeedKmh", "Latitude", "Longitude"
                    FROM telemetry_events
                    WHERE "Timestamp" >= {0} - make_interval(hours => {1})
                ),
                latest AS (
                    SELECT DISTINCT ON ("VehicleId")
                        "VehicleId", "Timestamp" AS last_seen_at, "SpeedKmh", "Latitude", "Longitude"
                    FROM recent
                    ORDER BY "VehicleId", "Timestamp" DESC
                ),
                last_move AS (
                    SELECT r."VehicleId", MAX(r."Timestamp") AS last_moving_at
                    FROM recent r
                    WHERE r."SpeedKmh" > {2}
                    GROUP BY r."VehicleId"
                ),
                first_stop_after_move AS (
                    SELECT DISTINCT ON (r."VehicleId")
                        r."VehicleId",
                        r."Timestamp" AS stopped_since
                    FROM recent r
                    INNER JOIN latest l ON l."VehicleId" = r."VehicleId"
                    LEFT JOIN last_move m ON m."VehicleId" = r."VehicleId"
                    WHERE r."SpeedKmh" <= {2}
                      AND r."Timestamp" > COALESCE(
                          m.last_moving_at,
                          {0} - make_interval(hours => {1}))
                      AND r."Timestamp" <= l.last_seen_at
                    ORDER BY r."VehicleId", r."Timestamp" ASC
                ),
                event_gaps AS (
                    SELECT
                        gaps."VehicleId",
                        MAX(gaps.gap) AS max_gap
                    FROM (
                        SELECT
                            r."VehicleId",
                            r."Timestamp" - LAG(r."Timestamp") OVER (
                                PARTITION BY r."VehicleId" ORDER BY r."Timestamp") AS gap
                        FROM recent r
                        INNER JOIN first_stop_after_move s ON s."VehicleId" = r."VehicleId"
                        INNER JOIN latest l ON l."VehicleId" = r."VehicleId"
                        WHERE r."Timestamp" BETWEEN s.stopped_since AND l.last_seen_at
                    ) gaps
                    WHERE gaps.gap IS NOT NULL
                    GROUP BY gaps."VehicleId"
                ),
                has_intermediate_move AS (
                    SELECT DISTINCT r."VehicleId"
                    FROM recent r
                    INNER JOIN first_stop_after_move s ON s."VehicleId" = r."VehicleId"
                    INNER JOIN latest l ON l."VehicleId" = r."VehicleId"
                    WHERE r."Timestamp" > s.stopped_since
                      AND r."Timestamp" < l.last_seen_at
                      AND r."SpeedKmh" > {2}
                )
                SELECT
                    l."VehicleId",
                    l.last_seen_at AS "LastSeenAt",
                    s.stopped_since AS "StoppedSince",
                    l."Latitude",
                    l."Longitude"
                FROM latest l
                INNER JOIN first_stop_after_move s ON s."VehicleId" = l."VehicleId"
                LEFT JOIN event_gaps g ON g."VehicleId" = l."VehicleId"
                LEFT JOIN has_intermediate_move im ON im."VehicleId" = l."VehicleId"
                WHERE l."SpeedKmh" <= {2}
                  AND im."VehicleId" IS NULL
                  AND l.last_seen_at >= {0} - make_interval(mins => {3})
                  AND (g.max_gap IS NULL OR g.max_gap <= make_interval(secs => {4}))
                  AND l.last_seen_at - s.stopped_since >= make_interval(mins => {5})
                ORDER BY l."VehicleId"
                """,
                asOf,
                lookbackHours,
                speedThreshold,
                freshnessMinutes,
                maxGapSeconds,
                minDurationMinutes)
            .ToListAsync(cancellationToken);

        var result = rows.Select(row =>
        {
            var duration = row.LastSeenAt - row.StoppedSince;
            var zone = CriticalZoneCatalog.FindZoneAt(row.Latitude, row.Longitude);
            return new StoppedVehicleStatusDto(
                row.VehicleId,
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
        string VehicleId,
        DateTimeOffset LastSeenAt,
        DateTimeOffset StoppedSince,
        double Latitude,
        double Longitude);
}
