using Confluent.Kafka;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Worker;

// Consume Kafka y delega el procesamiento a TelemetryMessageProcessor.
public class TelemetryConsumerWorker : BackgroundService
{
    private static readonly TimeSpan CircuitOpenBackoff = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly TelemetryMessageProcessor _messageProcessor;
    private readonly ILogger<TelemetryConsumerWorker> _logger;

    public TelemetryConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        ResiliencePipelineFactory resilience,
        TelemetryMessageProcessor messageProcessor,
        ILogger<TelemetryConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _resilience = resilience;
        _messageProcessor = messageProcessor;
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
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_kafkaOptions.TelemetryTopic);

        _logger.LogInformation(
            "Telemetry consumer started. Topic={Topic} DeadLetterTopic={DeadLetterTopic} Group={Group} MaxProcessingAttempts={MaxProcessingAttempts}",
            _kafkaOptions.TelemetryTopic,
            _kafkaOptions.DeadLetterTopic,
            _kafkaOptions.ConsumerGroup,
            _kafkaOptions.MaxProcessingAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? consumeResult = null;

            try
            {
                consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                    continue;

                var message = new KafkaConsumedMessage(
                    Payload: consumeResult.Message.Value,
                    Topic: consumeResult.Topic,
                    Partition: consumeResult.Partition.Value,
                    Offset: consumeResult.Offset.Value,
                    Key: consumeResult.Message.Key);

                var result = await _messageProcessor.ProcessAsync(
                    message,
                    async (telemetryEvent, token) =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var processUseCase = scope.ServiceProvider.GetRequiredService<ProcessTelemetryEventUseCase>();
                        return await _resilience.DatabaseProcessingPipeline.ExecuteAsync(
                            async pipelineToken => await processUseCase.ExecuteAsync(telemetryEvent, pipelineToken),
                            token);
                    },
                    stoppingToken);

                switch (result)
                {
                    case TelemetryMessageProcessingResult.ProcessedAndCommit:
                    case TelemetryMessageProcessingResult.SentToDeadLetterAndCommit:
                        consumer.Commit(consumeResult);
                        break;

                    case TelemetryMessageProcessingResult.RetryWithoutCommit:
                        await Task.Delay(CircuitOpenBackoff, stoppingToken);
                        break;

                    case TelemetryMessageProcessingResult.IgnoreWithoutCommit:
                        break;
                }
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error. Reason={Reason}", ex.Error.Reason);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Fallo al publicar DLQ u otro error inesperado: no confirmar offset.
                _logger.LogError(
                    ex,
                    "Unexpected consumer loop error; offset not committed. Topic={Topic} Partition={Partition} Offset={Offset}",
                    consumeResult?.Topic,
                    consumeResult?.Partition.Value,
                    consumeResult?.Offset.Value);
                await Task.Delay(CircuitOpenBackoff, stoppingToken);
            }
        }

        consumer.Close();
        _logger.LogInformation("Telemetry consumer stopped.");
    }
}
