using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

public interface ITelemetryRepository
{
    Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);

    Task<CursorPage<TelemetryEvent>> GetVehicleHistoryPageAsync(
        string vehicleId,
        DateTimeOffset from,
        DateTimeOffset to,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default);
}
