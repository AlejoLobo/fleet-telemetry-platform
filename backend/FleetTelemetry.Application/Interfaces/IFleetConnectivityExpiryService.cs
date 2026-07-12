namespace FleetTelemetry.Application.Interfaces;

// Publica transiciones offline cuando un vehículo cruza el umbral sin telemetría nueva.
public interface IFleetConnectivityExpiryService
{
    Task<int> PublishOfflineTransitionsAsync(CancellationToken cancellationToken = default);
}
