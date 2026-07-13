using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Alimenta SSE por polling a TimescaleDB con cursor estable de alertas.
public class FleetSsePollerHostedService : BackgroundService
{
    private readonly FleetSseBroker _broker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SseOptions _sseOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FleetSsePollerHostedService> _logger;
    private AlertStreamCursor _alertCursor = AlertStreamCursor.Origin;
    private string _lastFleetHash = string.Empty;
    private bool _fleetChangedLastPoll;

    public FleetSsePollerHostedService(
        FleetSseBroker broker,
        IServiceScopeFactory scopeFactory,
        IOptions<SseOptions> sseOptions,
        TimeProvider timeProvider,
        ILogger<FleetSsePollerHostedService> logger)
    {
        _broker = broker;
        _scopeFactory = scopeFactory;
        _sseOptions = sseOptions.Value;
        _timeProvider = timeProvider;
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
        if (_sseOptions.Mode == SseDeliveryMode.KafkaPush)
            return;

        var vehicles = await fleetQuery.GetAllFleetStatusesAsync(
            liveOnly: false,
            excludeSimulated: true,
            cancellationToken);
        var hash = string.Join('|', vehicles.Select(v =>
            $"{v.VehicleId}:{v.Status}:{v.LastSeenAt:o}:{v.LastSpeedKmh}:{v.LastLatitude}:{v.LastLongitude}"));

        _fleetChangedLastPoll = hash != _lastFleetHash;
        if (!_fleetChangedLastPoll)
            return;

        _lastFleetHash = hash;
        _broker.PublishLocal(FleetRealtimeEventTypes.FleetUpdate, vehicles, _timeProvider.GetUtcNow());
    }

    private async Task PublishNewAlertsAsync(IAlertRepository alertRepository, CancellationToken cancellationToken)
    {
        if (_sseOptions.Mode == SseDeliveryMode.KafkaPush)
            return;

        // Límite superior estable capturado antes de paginar para no perder alertas insertadas durante el ciclo.
        var upperBound = _timeProvider.GetUtcNow();

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await alertRepository.GetOpenAlertsAfterCursorAsync(
                _alertCursor,
                upperBound,
                _sseOptions.AlertBatchSize,
                cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var alert in batch)
            {
                _broker.PublishLocal(
                    FleetRealtimeEventTypes.Alert,
                    new FleetAlertResponse(
                        alert.AlertId,
                        alert.VehicleId,
                        alert.AlertType,
                        alert.Severity,
                        alert.Message,
                        alert.CreatedAt,
                        alert.IsAcknowledged),
                    _timeProvider.GetUtcNow());

                _alertCursor = AlertStreamCursor.FromAlert(alert.CreatedAt, alert.AlertId);
            }

            if (batch.Count < _sseOptions.AlertBatchSize)
                break;
        }
    }
}
