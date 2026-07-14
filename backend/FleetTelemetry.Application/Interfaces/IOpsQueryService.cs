using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IOpsQueryService
{
    Task<OpsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
}
