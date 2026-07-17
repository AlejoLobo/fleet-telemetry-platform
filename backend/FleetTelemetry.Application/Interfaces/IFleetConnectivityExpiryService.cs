namespace FleetTelemetry.Application.Interfaces;

public interface IFleetConnectivityExpiryService
{
    Task<int> PublishOfflineTransitionsAsync(CancellationToken cancellationToken = default);
}
