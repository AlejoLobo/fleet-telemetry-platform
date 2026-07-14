namespace FleetTelemetry.Application.Interfaces;

public interface IFleetRealtimePublisher
{
    Task PublishVehicleUpdateAsync(
        string vehicleId,
        string payloadJson,
        CancellationToken cancellationToken = default);

    Task PublishAlertAsync(
        string payloadJson,
        CancellationToken cancellationToken = default);
}
