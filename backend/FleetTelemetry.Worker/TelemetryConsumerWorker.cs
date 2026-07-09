using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Worker;

// Procesa mensajes de Kafka y persiste en TimescaleDB.
public class TelemetryConsumerWorker : BackgroundService
{
    private static readonly TimeSpan CircuitOpenBackoff = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly IKafkaDeadLetterPublisher _deadLetterPublisher;
    private readonly ILogger<TelemetryConsumerWorker> _logger;
    private readonly Dictionary<string, int> _processingAttempts = new();

    public TelemetryConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        ResiliencePipelineFactory resilience,
        IKafkaDeadLetterPublisher deadLetterPublisher,
        ILogger<TelemetryConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _resilience = resilience;
        _deadLetterPublisher = deadLetterPublisher;
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
            "Telemetry consumer started. Topic={Topic}, Dlq={Dlq}, Group={Group}",
            _kafkaOptions.TelemetryTopic,
            _kafkaOptions.DlqTopic,
            _kafkaOptions.ConsumerGroup);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? consumeResult = null;

            try
            {
                consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                    continue;

                var messageKey = BuildMessageKey(consumeResult);
                TelemetryEvent? telemetryEvent = null;

                try
                {
                    telemetryEvent = TelemetryEventJsonSerializer.Deserialize(consumeResult.Message.Value);
                }
                catch (InvalidOperationException ex)
                {
                    await SendToDlqAndCommitAsync(
                        consumer,
                        consumeResult,
                        failureReason: ex.Message,
                        failureType: "deserialization",
                        attemptCount: 1,
                        stoppingToken);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var processUseCase = scope.ServiceProvider.GetRequiredService<ProcessTelemetryEventUseCase>();

                var outcome = await _resilience.DatabaseProcessingPipeline.ExecuteAsync(
                    async token => await processUseCase.ExecuteAsync(telemetryEvent, token),
                    stoppingToken);

                consumer.Commit(consumeResult);
                _processingAttempts.Remove(messageKey);

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
                if (consumeResult is not null)
                {
                    await SendToDlqAndCommitAsync(
                        consumer,
                        consumeResult,
                        failureReason: ex.Message,
                        failureType: "validation",
                        attemptCount: 1,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (consumeResult is null)
                {
                    _logger.LogError(ex, "Unexpected error without consume result");
                    continue;
                }

                var messageKey = BuildMessageKey(consumeResult);
                var attempts = _processingAttempts.TryGetValue(messageKey, out var count) ? count + 1 : 1;
                _processingAttempts[messageKey] = attempts;

                if (attempts >= _kafkaOptions.MaxProcessingAttempts)
                {
                    await SendToDlqAndCommitAsync(
                        consumer,
                        consumeResult,
                        failureReason: ex.Message,
                        failureType: "processing",
                        attemptCount: attempts,
                        stoppingToken);
                    _processingAttempts.Remove(messageKey);
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Error procesando mensaje {Key}; intento {Attempt}/{Max}. Offset no confirmado",
                        messageKey,
                        attempts,
                        _kafkaOptions.MaxProcessingAttempts);
                }
            }
        }

        consumer.Close();
        _logger.LogInformation("Telemetry consumer stopped.");
    }

    private async Task SendToDlqAndCommitAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> consumeResult,
        string failureReason,
        string failureType,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        var dlqMessage = new DeadLetterMessage(
            SourceTopic: consumeResult.Topic,
            Partition: consumeResult.Partition.Value,
            Offset: consumeResult.Offset.Value,
            OriginalKey: consumeResult.Message.Key,
            OriginalPayload: consumeResult.Message.Value,
            FailureReason: failureReason,
            FailureType: failureType,
            AttemptCount: attemptCount,
            FailedAt: DateTimeOffset.UtcNow);

        await _deadLetterPublisher.PublishAsync(dlqMessage, cancellationToken);
        consumer.Commit(consumeResult);

        _logger.LogWarning(
            "Mensaje enviado a DLQ y offset confirmado: topic={Topic} partition={Partition} offset={Offset} type={Type}",
            consumeResult.Topic,
            consumeResult.Partition.Value,
            consumeResult.Offset.Value,
            failureType);
    }

    private static string BuildMessageKey(ConsumeResult<string, string> result) =>
        $"{result.Topic}:{result.Partition.Value}:{result.Offset.Value}";
}
