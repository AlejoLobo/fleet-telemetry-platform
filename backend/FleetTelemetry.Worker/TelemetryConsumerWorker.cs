using Confluent.Kafka;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

// Worker en segundo plano que consume telemetría desde Kafka.
namespace FleetTelemetry.Worker;

// Procesa mensajes de Kafka y persiste en TimescaleDB.
public class TelemetryConsumerWorker : BackgroundService
{
    private static readonly TimeSpan CircuitOpenBackoff = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly ILogger<TelemetryConsumerWorker> _logger;

    public TelemetryConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        ResiliencePipelineFactory resilience,
        ILogger<TelemetryConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _resilience = resilience;
        _logger = logger;
    }

    // Bucle principal: consume, procesa y confirma offsets.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Inicializa esquema de base de datos al arrancar.
        using (var initScope = _scopeFactory.CreateScope())
        {
            await DatabaseInitializer.InitializeAsync(initScope.ServiceProvider, stoppingToken);
        }

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
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
            ConsumeResult<string, string>? consumeResult = null;

            try
            {
                consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                    continue;

                var telemetryEvent = TelemetryEventJsonSerializer.Deserialize(consumeResult.Message.Value);

                using var scope = _scopeFactory.CreateScope();
                var processUseCase = scope.ServiceProvider.GetRequiredService<ProcessTelemetryEventUseCase>();

                var outcome = await _resilience.DatabaseProcessingPipeline.ExecuteAsync(
                    async token => await processUseCase.ExecuteAsync(telemetryEvent, token),
                    stoppingToken);

                consumer.Commit(consumeResult);

                if (outcome == ProcessTelemetryOutcome.Processed)
                {
                    _logger.LogInformation(
                        "Telemetry event processed: {EventId} vehicle {VehicleId}",
                        telemetryEvent.EventId,
                        telemetryEvent.VehicleId);
                }
            }
            catch (BrokenCircuitException ex)
            {
                // No confirma offset si la base de datos no está disponible.
                _logger.LogWarning(
                    ex,
                    "TimescaleDB circuit breaker abierto; offset no confirmado. Reintento en {Seconds}s",
                    CircuitOpenBackoff.TotalSeconds);
                await Task.Delay(CircuitOpenBackoff, stoppingToken);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Skipping message with invalid telemetry payload");
                if (consumeResult is not null)
                    consumer.Commit(consumeResult);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing telemetry message; offset not committed");
            }
        }

        consumer.Close();
        _logger.LogInformation("Telemetry consumer stopped.");
    }
}
