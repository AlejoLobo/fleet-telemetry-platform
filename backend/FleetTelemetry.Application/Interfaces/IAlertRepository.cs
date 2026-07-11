using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

// Contrato de persistencia de alertas.
namespace FleetTelemetry.Application.Interfaces;

// Acceso a alertas abiertas y confirmación.
public interface IAlertRepository
{
    // Obtiene alertas sin confirmar.
    Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default);

    // Obtiene alertas abiertas posteriores al cursor, con límite superior fijo antes de consultar.
    Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAfterCursorAsync(
        AlertStreamCursor cursor,
        DateTimeOffset upperBound,
        int limit,
        CancellationToken cancellationToken = default);

    // Marca una alerta como confirmada.
    Task<bool> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken = default);
    // Guarda una alerta nueva.
    Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default);
}
