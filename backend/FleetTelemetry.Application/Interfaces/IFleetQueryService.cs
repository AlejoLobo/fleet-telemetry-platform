using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IFleetQueryService
{
    Task<IReadOnlyList<VehicleLatestStatusResponse>> GetLatestVehicleStatusesAsync(CancellationToken cancellationToken = default);
    Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(string vehicleId, CancellationToken cancellationToken = default);
}
