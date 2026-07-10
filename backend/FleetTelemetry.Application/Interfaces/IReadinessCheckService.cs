using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Interfaces;

// Comprueba dependencias críticas para readiness (sin exponer secretos).
public interface IReadinessCheckService
{
    Task<ReadinessCheckResponse> CheckAsync(CancellationToken cancellationToken = default);
}
