using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Consulta flota y alertas periódicamente y publica actualizaciones SSE.
public class FleetSsePollerHostedService : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly FleetSseBroker _broker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SseOptions _sseOptions;
    private readonly ILogger<FleetSsePollerHostedService> _logger;
    private DateTimeOffset _lastAlertCheck = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
    private string _lastFleetHash = string.Empty;
    private bool _fleetChangedLastPoll;

    public FleetSsePollerHostedService(
        FleetSseBroker broker,
        IServiceScopeFactory scopeFactory,
        IOptions<SseOptions> sseOptions,
        ILogger<FleetSsePollerHostedService> logger)
    {
        _broker = broker;
        _scopeFactory = scopeFactory;
        _sseOptions = sseOptions.Value;
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
                    await Task.Delay(TimeSpan.FromSeconds(_sseOptions.ActivePollIntervalSeconds), stoppingToken);
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

            var delaySeconds = _fleetChangedLastPoll
                ? _sseOptions.ActivePollIntervalSeconds
                : _sseOptions.IdlePollIntervalSeconds;

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private async Task PublishFleetUpdatesAsync(IFleetQueryService fleetQuery, CancellationToken cancellationToken)
    {
        var vehicles = await fleetQuery.GetLatestVehicleStatusesAsync(liveOnly: false, cancellationToken);
        var hash = string.Join('|', vehicles.Select(v =>
            $"{v.VehicleId}:{v.Status}:{v.LastSeenAt:o}:{v.LastSpeedKmh}:{v.LastLatitude}:{v.LastLongitude}"));

        _fleetChangedLastPoll = hash != _lastFleetHash;
        if (!_fleetChangedLastPoll)
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
