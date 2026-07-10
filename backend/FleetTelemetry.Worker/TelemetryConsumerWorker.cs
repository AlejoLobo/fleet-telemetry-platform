using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Worker;

// Procesa mensajes de Kafka y persiste en TimescaleDB.
public class TelemetryConsumerWorker : BackgroundService
{
    private static readonly TimeSpan CircuitOpenBackoff = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly IDeadLetterPublisher _deadLetterPublisher;
    private readonly ILogger<TelemetryConsumerWorker> _logger;
    private readonly Dictionary<string, int> _processingAttempts = new();

    public TelemetryConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        ResiliencePipelineFactory resilience,
        IDeadLetterPublisher deadLetterPublisher,
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

                var messageKey = BuildMessageKey(consumeResult);

                TelemetryEvent telemetryEvent;
                try
                {
                    telemetryEvent = TelemetryEventJsonSerializer.Deserialize(consumeResult.Message.Value);
                }
                catch (InvalidOperationException ex)
                {
                    await PublishDeadLetterAndCommitAsync(
                        consumer,
                        consumeResult,
                        reason: "invalid_payload",
                        exceptionMessage: ex.Message,
                        cancellationToken: stoppingToken);
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
                        "Telemetry event processed. EventId={EventId} VehicleId={VehicleId} Partition={Partition} Offset={Offset}",
                        telemetryEvent.EventId,
                        telemetryEvent.VehicleId,
                        consumeResult.Partition.Value,
                        consumeResult.Offset.Value);
                }
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogWarning(
                    ex,
                    "TimescaleDB circuit breaker open; offset not committed. BackoffSeconds={BackoffSeconds} Topic={Topic} Partition={Partition} Offset={Offset}",
                    CircuitOpenBackoff.TotalSeconds,
                    consumeResult?.Topic,
                    consumeResult?.Partition.Value,
                    consumeResult?.Offset.Value);
                await Task.Delay(CircuitOpenBackoff, stoppingToken);
            }
            catch (Exception ex) when (IsInfrastructureTransientFailure(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Transient infrastructure failure; offset not committed and message not sent to DLQ. Topic={Topic} Partition={Partition} Offset={Offset}",
                    consumeResult?.Topic,
                    consumeResult?.Partition.Value,
                    consumeResult?.Offset.Value);
                await Task.Delay(CircuitOpenBackoff, stoppingToken);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error. Reason={Reason}", ex.Error.Reason);
            }
            catch (InvalidOperationException ex)
            {
                if (consumeResult is not null)
                {
                    await PublishDeadLetterAndCommitAsync(
                        consumer,
                        consumeResult,
                        reason: "validation_error",
                        exceptionMessage: ex.Message,
                        cancellationToken: stoppingToken);
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
                    _logger.LogError(ex, "Unexpected processing error without consume result");
                    continue;
                }

                var messageKey = BuildMessageKey(consumeResult);
                var attempts = _processingAttempts.TryGetValue(messageKey, out var count) ? count + 1 : 1;
                _processingAttempts[messageKey] = attempts;

                if (attempts >= _kafkaOptions.MaxProcessingAttempts)
                {
                    await PublishDeadLetterAndCommitAsync(
                        consumer,
                        consumeResult,
                        reason: "processing_failure",
                        exceptionMessage: ex.Message,
                        cancellationToken: stoppingToken);
                    _processingAttempts.Remove(messageKey);
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Non-transient processing error; retry pending. MessageKey={MessageKey} Attempt={Attempt} MaxAttempts={MaxAttempts} Topic={Topic} Partition={Partition} Offset={Offset}",
                        messageKey,
                        attempts,
                        _kafkaOptions.MaxProcessingAttempts,
                        consumeResult.Topic,
                        consumeResult.Partition.Value,
                        consumeResult.Offset.Value);
                }
            }
        }

        consumer.Close();
        _logger.LogInformation("Telemetry consumer stopped.");
    }

    private async Task PublishDeadLetterAndCommitAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> consumeResult,
        string reason,
        string exceptionMessage,
        CancellationToken cancellationToken)
    {
        var deadLetterMessage = new DeadLetterMessage(
            OriginalPayload: consumeResult.Message.Value,
            Reason: reason,
            ExceptionMessage: exceptionMessage,
            OriginalTopic: consumeResult.Topic,
            Partition: consumeResult.Partition.Value,
            Offset: consumeResult.Offset.Value,
            OccurredAt: DateTimeOffset.UtcNow);

        // Offset se confirma solo si la publicación en DLQ fue exitosa.
        await _deadLetterPublisher.PublishAsync(deadLetterMessage, cancellationToken);
        consumer.Commit(consumeResult);

        _logger.LogWarning(
            "Message moved to dead letter queue and offset committed. Reason={Reason} OriginalTopic={OriginalTopic} Partition={Partition} Offset={Offset} DeadLetterTopic={DeadLetterTopic}",
            reason,
            consumeResult.Topic,
            consumeResult.Partition.Value,
            consumeResult.Offset.Value,
            _kafkaOptions.DeadLetterTopic);
    }

    private static bool IsInfrastructureTransientFailure(Exception ex) =>
        ex is NpgsqlException or DbUpdateException or TimeoutException;

    private static string BuildMessageKey(ConsumeResult<string, string> result) =>
        $"{result.Topic}:{result.Partition.Value}:{result.Offset.Value}";
}
