namespace FleetTelemetry.Application.Interfaces;

public sealed class NoOpFleetRealtimePublisher : IFleetRealtimePublisher
{
    public Task PublishVehicleUpdateAsync(Guid deviceId, string payloadJson, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishAlertAsync(string payloadJson, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
