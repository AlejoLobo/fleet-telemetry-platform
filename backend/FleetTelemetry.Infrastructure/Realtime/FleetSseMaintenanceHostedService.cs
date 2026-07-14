using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Heartbeat efímero y poda de suscriptores inactivos (KafkaPush y Polling).
public sealed class FleetSseMaintenanceHostedService : BackgroundService
{
    private readonly FleetSseBroker _broker;
    private readonly SseOptions _sseOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FleetSseMaintenanceHostedService> _logger;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;

    public FleetSseMaintenanceHostedService(
        FleetSseBroker broker,
        IOptions<SseOptions> sseOptions,
        TimeProvider timeProvider,
        ILogger<FleetSseMaintenanceHostedService> logger)
    {
        _broker = broker;
        _sseOptions = sseOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    internal DateTimeOffset LastHeartbeat => _lastHeartbeat;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _sseOptions.HeartbeatIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _broker.PruneStaleSubscribers(stoppingToken);

                if (_broker.SubscriberCount > 0)
                    PublishHeartbeatIfNeeded();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SSE maintenance cycle failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private void PublishHeartbeatIfNeeded()
    {
        var heartbeatInterval = TimeSpan.FromSeconds(_sseOptions.HeartbeatIntervalSeconds);
        var now = _timeProvider.GetUtcNow();
        if (now - _lastHeartbeat < heartbeatInterval)
            return;

        _lastHeartbeat = now;
        _broker.PublishEphemeral(
            FleetRealtimeEventTypes.Heartbeat,
            new { status = "ok", subscribers = _broker.SubscriberCount },
            now);
    }
}
