using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IFleetQueryService
{
    /// <param name="liveOnly">Si es true, solo devuelve vehículos con telemetría en los últimos 5 minutos.</param>
    Task<IReadOnlyList<VehicleLatestStatusResponse>> GetLatestVehicleStatusesAsync(
        bool liveOnly = false,
        CancellationToken cancellationToken = default);
    Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(string vehicleId, CancellationToken cancellationToken = default);
}
