using FleetTelemetry.Application.DTOs;

// Contrato de consultas de estado de flota.
namespace FleetTelemetry.Application.Interfaces;

// Último estado conocido por vehículo.
public interface IFleetQueryService
{
    /// <param name="liveOnly">Si es true, solo devuelve vehículos con telemetría en la ventana online.</param>
    Task<CursorPage<VehicleLatestStatusResponse>> GetFleetPageAsync(
        int pageSize,
        string? cursor,
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default);

    // Recolecta todas las páginas para consumidores internos (SSE, IA).
    Task<IReadOnlyList<VehicleLatestStatusResponse>> GetAllFleetStatusesAsync(
        bool liveOnly = false,
        bool excludeSimulated = false,
        CancellationToken cancellationToken = default);

    Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
        string vehicleId,
        CancellationToken cancellationToken = default);
}
