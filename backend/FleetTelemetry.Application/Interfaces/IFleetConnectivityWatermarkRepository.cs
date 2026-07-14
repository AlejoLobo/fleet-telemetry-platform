namespace FleetTelemetry.Application.Interfaces;

public interface IFleetConnectivityWatermarkRepository
{
    Task<DateTimeOffset?> GetPreviousOnlineThresholdAsync(CancellationToken cancellationToken = default);

    Task SetPreviousOnlineThresholdAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default);
}
