namespace FleetTelemetry.Application.Interfaces;

// Publica eventos de tiempo real hacia un backplane desacoplado (Kafka en producción).
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
