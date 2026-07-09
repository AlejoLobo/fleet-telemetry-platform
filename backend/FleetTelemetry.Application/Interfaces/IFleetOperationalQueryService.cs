using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IFleetOperationalQueryService
{
    /// <summary>
    /// Vehículos cuya última telemetría indica detención continua al menos <paramref name="minDuration"/>.
    /// </summary>
    Task<IReadOnlyList<StoppedVehicleStatusDto>> GetVehiclesStoppedLongerThanAsync(
        TimeSpan minDuration,
        double stoppedSpeedThresholdKmh = 1,
        CancellationToken cancellationToken = default);
}
