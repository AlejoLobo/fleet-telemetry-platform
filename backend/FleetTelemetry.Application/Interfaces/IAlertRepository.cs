using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

public interface IAlertRepository
{
    Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default);
    Task<FleetAlert?> GetByIdAsync(Guid alertId, CancellationToken cancellationToken = default);
    Task<bool> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken = default);
    Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default);
}
