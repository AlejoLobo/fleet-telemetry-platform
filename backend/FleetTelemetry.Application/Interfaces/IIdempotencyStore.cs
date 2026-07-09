// Contrato de almacén de idempotencia por evento.
namespace FleetTelemetry.Application.Interfaces;

// Evita procesar el mismo evento dos veces.
public interface IIdempotencyStore
{
    // Intenta reservar el identificador; devuelve false si ya existe.
    Task<bool> TryAcquireAsync(Guid eventId, CancellationToken cancellationToken = default);
}
