using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Mocks;

public class MockAlertRepository : IAlertRepository
{
    private readonly ILogger<MockAlertRepository> _logger;

    public MockAlertRepository(ILogger<MockAlertRepository> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] GetOpenAlertsAsync called (returning empty list)");

        IReadOnlyList<FleetAlert> result = Array.Empty<FleetAlert>();
        return Task.FromResult(result);
    }

    public Task<FleetAlert?> GetByIdAsync(Guid alertId, CancellationToken cancellationToken = default) =>
        Task.FromResult<FleetAlert?>(null);

    public Task<bool> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MOCK] SaveAsync called for alert {AlertId} (persistence not implemented in Phase 1)",
            alert.AlertId);

        return Task.CompletedTask;
    }
}
