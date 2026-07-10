using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

// Resumen operativo agregado a partir de consultas existentes.
public interface IOpsQueryService
{
    Task<OpsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
}
