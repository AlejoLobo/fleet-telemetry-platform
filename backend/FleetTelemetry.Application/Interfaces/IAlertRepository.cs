using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

public interface IAlertRepository
{
    Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default);
}
