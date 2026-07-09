using Confluent.Kafka;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Worker;

public class TelemetryConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<TelemetryConsumerWorker> _logger;

    public TelemetryConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        ILogger<TelemetryConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var initScope = _scopeFactory.CreateScope())
        {
            await DatabaseInitializer.InitializeAsync(initScope.ServiceProvider, stoppingToken);
        }

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_kafkaOptions.TelemetryTopic);

        _logger.LogInformation(
            "Telemetry consumer started. Topic={Topic}, Group={Group}, Bootstrap={Bootstrap}",
            _kafkaOptions.TelemetryTopic,
            _kafkaOptions.ConsumerGroup,
            _kafkaOptions.BootstrapServers);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                    continue;

                var telemetryEvent = TelemetryEventJsonSerializer.Deserialize(consumeResult.Message.Value);

                using var scope = _scopeFactory.CreateScope();
                var processUseCase = scope.ServiceProvider.GetRequiredService<ProcessTelemetryEventUseCase>();
                var processed = await processUseCase.ExecuteAsync(telemetryEvent, stoppingToken);

                if (processed)
                {
                    _logger.LogInformation(
                        "Telemetry event processed: {EventId} vehicle {VehicleId}",
                        telemetryEvent.EventId,
                        telemetryEvent.VehicleId);
                }
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing telemetry message");
            }
        }

        consumer.Close();
        _logger.LogInformation("Telemetry consumer stopped.");
    }
}
