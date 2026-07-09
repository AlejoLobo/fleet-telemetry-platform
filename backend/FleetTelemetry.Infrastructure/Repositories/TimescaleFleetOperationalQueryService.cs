using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// Consultas operativas de vehículos detenidos.
namespace FleetTelemetry.Infrastructure.Repositories;

// Detecta detenciones prolongadas con SQL analítico.
public class TimescaleFleetOperationalQueryService : IFleetOperationalQueryService
{
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromHours(48);

    private readonly FleetDbContext _dbContext;
    private readonly ILogger<TimescaleFleetOperationalQueryService> _logger;

    public TimescaleFleetOperationalQueryService(
        FleetDbContext dbContext,
        ILogger<TimescaleFleetOperationalQueryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StoppedVehicleStatusDto>> GetVehiclesStoppedLongerThanAsync(
        TimeSpan minDuration,
        double stoppedSpeedThresholdKmh = 1,
        CancellationToken cancellationToken = default)
    {
        var lookbackHours = (int)Math.Ceiling(LookbackWindow.TotalHours);
        var minDurationMinutes = (int)Math.Ceiling(minDuration.TotalMinutes);

        // stopped_since = último evento con velocidad > umbral; si no hay, inicio de ventana de baja velocidad.
        var rows = await _dbContext.Database
            .SqlQueryRaw<StoppedVehicleRow>(
                """
                WITH latest AS (
                    SELECT DISTINCT ON ("VehicleId")
                        "VehicleId", "Timestamp", "SpeedKmh", "Latitude", "Longitude"
                    FROM telemetry_events
                    ORDER BY "VehicleId", "Timestamp" DESC
                ),
                last_move AS (
                    SELECT e."VehicleId", MAX(e."Timestamp") AS last_moving_at
                    FROM telemetry_events e
                    INNER JOIN latest l ON l."VehicleId" = e."VehicleId"
                    WHERE e."SpeedKmh" > {0}
                      AND e."Timestamp" >= l."Timestamp" - make_interval(hours => {1})
                    GROUP BY e."VehicleId"
                ),
                stopped_start AS (
                    SELECT
                        l."VehicleId",
                        l."Timestamp" AS last_seen_at,
                        l."Latitude",
                        l."Longitude",
                        COALESCE(
                            m.last_moving_at,
                            (
                                SELECT MIN(e2."Timestamp")
                                FROM telemetry_events e2
                                WHERE e2."VehicleId" = l."VehicleId"
                                  AND e2."SpeedKmh" <= {0}
                                  AND e2."Timestamp" >= l."Timestamp" - make_interval(hours => {1})
                            ),
                            l."Timestamp" - make_interval(hours => {1})
                        ) AS stopped_since
                    FROM latest l
                    LEFT JOIN last_move m ON m."VehicleId" = l."VehicleId"
                    WHERE l."SpeedKmh" <= {0}
                )
                SELECT
                    "VehicleId",
                    last_seen_at AS "LastSeenAt",
                    stopped_since AS "StoppedSince",
                    "Latitude",
                    "Longitude"
                FROM stopped_start
                WHERE last_seen_at - stopped_since >= make_interval(mins => {2})
                ORDER BY "VehicleId"
                """,
                stoppedSpeedThresholdKmh,
                lookbackHours,
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
            "Consultados {Count} vehículos detenidos ≥ {Minutes} min (umbral {Speed} km/h)",
            result.Count,
            minDurationMinutes,
            stoppedSpeedThresholdKmh);

        return result;
    }

    private sealed record StoppedVehicleRow(
        string VehicleId,
        DateTimeOffset LastSeenAt,
        DateTimeOffset StoppedSince,
        double Latitude,
        double Longitude);
}
