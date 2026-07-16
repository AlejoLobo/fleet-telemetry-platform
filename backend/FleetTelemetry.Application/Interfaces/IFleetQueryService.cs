using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IFleetQueryService
{
    /// <param name="liveOnly">Si es true, solo devuelve vehículos con telemetría en la ventana online.</param>
    Task<CursorPage<VehicleLatestStatusResponse>> GetFleetPageAsync(
        int pageSize,
        string? cursor,
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VehicleLatestStatusResponse>> GetAllFleetStatusesAsync(
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default);

    Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
