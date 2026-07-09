using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Realtime;

public class FleetSsePollerHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly FleetSseBroker _broker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FleetSsePollerHostedService> _logger;
    private DateTimeOffset _lastAlertCheck = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
    private string _lastFleetHash = string.Empty;

    public FleetSsePollerHostedService(
        FleetSseBroker broker,
        IServiceScopeFactory scopeFactory,
        ILogger<FleetSsePollerHostedService> logger)
    {
        _broker = broker;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_broker.SubscriberCount == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var fleetQuery = scope.ServiceProvider.GetRequiredService<IFleetQueryService>();
                var alertRepository = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

                await PublishFleetUpdatesAsync(fleetQuery, stoppingToken);
                await PublishNewAlertsAsync(alertRepository, stoppingToken);
                await PublishHeartbeatIfNeededAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en poller SSE de flota");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PublishFleetUpdatesAsync(IFleetQueryService fleetQuery, CancellationToken cancellationToken)
    {
        var vehicles = await fleetQuery.GetLatestVehicleStatusesAsync(cancellationToken);
        var hash = string.Join('|', vehicles.Select(v =>
            $"{v.VehicleId}:{v.Status}:{v.LastSeenAt:o}:{v.LastSpeedKmh}"));

        if (hash == _lastFleetHash)
            return;

        _lastFleetHash = hash;
        await _broker.PublishAsync(new FleetSseEvent(
            "fleet-update",
            vehicles,
            DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task PublishNewAlertsAsync(IAlertRepository alertRepository, CancellationToken cancellationToken)
    {
        var alerts = await alertRepository.GetOpenAlertsAsync(cancellationToken);
        var newAlerts = alerts.Where(a => a.CreatedAt > _lastAlertCheck).ToList();
        _lastAlertCheck = DateTimeOffset.UtcNow;

        foreach (var alert in newAlerts)
        {
            await _broker.PublishAsync(new FleetSseEvent(
                "alert",
                new FleetAlertResponse(
                    alert.AlertId,
                    alert.VehicleId,
                    alert.AlertType,
                    alert.Severity,
                    alert.Message,
                    alert.CreatedAt,
                    alert.IsAcknowledged),
                DateTimeOffset.UtcNow), cancellationToken);
        }
    }

    private async Task PublishHeartbeatIfNeededAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _lastHeartbeat < HeartbeatInterval)
            return;

        _lastHeartbeat = DateTimeOffset.UtcNow;
        await _broker.PublishAsync(new FleetSseEvent(
            "heartbeat",
            new { status = "ok" },
            _lastHeartbeat), cancellationToken);
    }
}
