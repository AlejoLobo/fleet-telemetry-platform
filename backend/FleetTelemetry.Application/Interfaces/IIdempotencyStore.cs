// Contrato de almacén de idempotencia por evento.
namespace FleetTelemetry.Application.Interfaces;

public interface IIdempotencyStore
{
    Task<bool> TryAcquireAsync(Guid eventId, CancellationToken cancellationToken = default);
}
