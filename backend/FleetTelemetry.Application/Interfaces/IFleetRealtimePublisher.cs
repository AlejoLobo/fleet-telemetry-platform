namespace FleetTelemetry.Application.Interfaces;

public interface IFleetRealtimePublisher
{
    Task PublishVehicleUpdateAsync(
        Guid deviceId,
        string payloadJson,
        CancellationToken cancellationToken = default);

    Task PublishAlertAsync(
        string payloadJson,
        CancellationToken cancellationToken = default);
}
