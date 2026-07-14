using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IAiAgentService
{
    Task<AiQueryResponse> QueryAsync(AiQueryRequest request, CancellationToken cancellationToken = default);
}
