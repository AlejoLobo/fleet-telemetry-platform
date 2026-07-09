using FleetTelemetry.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Application.UseCases;

public class AcknowledgeAlertUseCase
{
    private readonly IAlertRepository _alertRepository;
    private readonly ILogger<AcknowledgeAlertUseCase> _logger;

    public AcknowledgeAlertUseCase(IAlertRepository alertRepository, ILogger<AcknowledgeAlertUseCase> logger)
    {
        _alertRepository = alertRepository;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(Guid alertId, CancellationToken cancellationToken = default)
    {
        var acknowledged = await _alertRepository.AcknowledgeAsync(alertId, cancellationToken);
        if (acknowledged)
            _logger.LogInformation("Alerta {AlertId} confirmada", alertId);

        return acknowledged;
    }
}
