using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

// Contrato de persistencia de telemetría.
namespace FleetTelemetry.Application.Interfaces;

// Lectura y escritura de eventos históricos.
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
