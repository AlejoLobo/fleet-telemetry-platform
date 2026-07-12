using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Realtime;

// Ciclo periódico de expiración de conectividad para KafkaPush.
public sealed class FleetConnectivityExpiryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SseOptions _sseOptions;
    private readonly ILogger<FleetConnectivityExpiryHostedService> _logger;

    public FleetConnectivityExpiryHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<SseOptions> sseOptions,
        ILogger<FleetConnectivityExpiryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _sseOptions = sseOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_sseOptions.ConnectivityExpiryIntervalSeconds);
        _logger.LogInformation(
            "Fleet connectivity expiry service started (interval={IntervalSeconds}s).",
            _sseOptions.ConnectivityExpiryIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var expiryService = scope.ServiceProvider.GetRequiredService<IFleetConnectivityExpiryService>();
                var published = await expiryService.PublishOfflineTransitionsAsync(stoppingToken);

                if (published > 0)
                {
                    _logger.LogInformation(
                        "Published {Count} offline connectivity transition(s).",
                        published);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Connectivity expiry cycle failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Fleet connectivity expiry service stopped.");
    }
}
