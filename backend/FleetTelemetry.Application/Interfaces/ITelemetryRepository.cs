using FleetTelemetry.Domain.Entities;

// Contrato de persistencia de telemetría.
namespace FleetTelemetry.Application.Interfaces;

// Lectura y escritura de eventos históricos.
public interface ITelemetryRepository
{
    // Guarda un evento en almacenamiento.
    Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
    // Consulta eventos de un vehículo en un rango.
    Task<IReadOnlyList<TelemetryEvent>> GetByVehicleAsync(string vehicleId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
