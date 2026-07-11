namespace FleetTelemetry.Application.Interfaces;

// No publica eventos; usado cuando el push realtime está deshabilitado.
public sealed class NoOpFleetRealtimePublisher : IFleetRealtimePublisher
{
    public Task PublishVehicleUpdateAsync(string vehicleId, string payloadJson, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishAlertAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
