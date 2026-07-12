namespace FleetTelemetry.Application.Interfaces;

// Persiste el umbral procesado por el servicio de expiración de conectividad.
public interface IFleetConnectivityWatermarkRepository
{
    Task<DateTimeOffset?> GetPreviousOnlineThresholdAsync(CancellationToken cancellationToken = default);

    Task SetPreviousOnlineThresholdAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default);
}
