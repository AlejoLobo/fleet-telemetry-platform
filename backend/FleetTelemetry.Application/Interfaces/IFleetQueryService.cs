using FleetTelemetry.Application.DTOs;

// Contrato de consultas de estado de flota.
namespace FleetTelemetry.Application.Interfaces;

// Último estado conocido por vehículo.
public interface IFleetQueryService
{
    /// <param name="liveOnly">Si es true, solo devuelve vehículos con telemetría en los últimos 5 minutos.</param>
    // Lista estados recientes de todos los vehículos.
    Task<IReadOnlyList<VehicleLatestStatusResponse>> GetLatestVehicleStatusesAsync(
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default);
    // Obtiene estado de un vehículo por identificador.
    Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(string vehicleId, CancellationToken cancellationToken = default);
}
