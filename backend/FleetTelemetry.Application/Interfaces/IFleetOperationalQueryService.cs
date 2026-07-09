using FleetTelemetry.Application.DTOs;

// Contrato de consultas operativas de flota.
namespace FleetTelemetry.Application.Interfaces;

// Detección de vehículos detenidos por duración.
public interface IFleetOperationalQueryService
{
    /// <summary>
    /// Vehículos cuya última telemetría indica detención continua al menos <paramref name="minDuration"/>.
    /// </summary>
    // Lista vehículos detenidos más tiempo que el umbral.
    Task<IReadOnlyList<StoppedVehicleStatusDto>> GetVehiclesStoppedLongerThanAsync(
        TimeSpan minDuration,
        double stoppedSpeedThresholdKmh = 1,
        CancellationToken cancellationToken = default);
}
