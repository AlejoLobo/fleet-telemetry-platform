using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

public interface IAlertRepository
{
    Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default);

    // Obtiene alertas abiertas posteriores al cursor, con límite superior fijo antes de consultar.
    Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAfterCursorAsync(
        AlertStreamCursor cursor,
        DateTimeOffset upperBound,
        int limit,
        CancellationToken cancellationToken = default);

    Task<bool> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken = default);
    Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default);
}
