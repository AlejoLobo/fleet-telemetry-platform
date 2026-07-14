using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

public interface IReadinessCheckService
{
    Task<ReadinessCheckResponse> CheckAsync(CancellationToken cancellationToken = default);
}
