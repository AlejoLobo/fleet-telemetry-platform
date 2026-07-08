namespace FleetTelemetry.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fase 2: consumir Kafka y persistir en TimescaleDB
        _logger.LogInformation(
            "FleetTelemetry.Worker started. Kafka consumer will be implemented in Phase 2.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
