using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Worker;

// Orquesta procesamiento y publicación DLQ sin mezclar reintentos ni reiniciar intentos de negocio.
public sealed class TelemetryMessageCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly TelemetryMessageProcessor _messageProcessor;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TelemetryMessageCoordinator> _logger;

    public TelemetryMessageCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        ResiliencePipelineFactory resilience,
        TelemetryMessageProcessor messageProcessor,
        IHostApplicationLifetime lifetime,
        ILogger<TelemetryMessageCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _resilience = resilience;
        _messageProcessor = messageProcessor;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task<CoordinatorResult> ProcessUntilTerminalAsync(
        KafkaConsumedMessage message,
        CancellationToken stoppingToken) =>
        ProcessUntilTerminalAsync(
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

    public async Task<CoordinatorResult> ProcessUntilTerminalAsync(
        KafkaConsumedMessage message,
        Func<TelemetryEvent, CancellationToken, Task<ProcessTelemetryOutcome>> processEvent,
        CancellationToken stoppingToken)
    {
        try
        {
            return await ProcessUntilTerminalCoreAsync(message, processEvent, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return CoordinatorResult.CancelledWithoutCommit;
        }
        catch (Exception ex)
        {
            // Error de programación u otro fallo no clasificado: no DLQ, no commit, detener el host.
            _logger.LogCritical(
                ex,
                "Unexpected processing error; stopping worker without commit. ExceptionType={ExceptionType} Topic={Topic} Partition={Partition} Offset={Offset}",
                ex.GetType().FullName,
                message.Topic,
                message.Partition,
                message.Offset);
            _lifetime.StopApplication();
            return CoordinatorResult.StopWithoutCommit;
        }
    }

    private async Task<CoordinatorResult> ProcessUntilTerminalCoreAsync(
        KafkaConsumedMessage message,
        Func<TelemetryEvent, CancellationToken, Task<ProcessTelemetryOutcome>> processEvent,
        CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;

            var outcome = await _messageProcessor.ProcessAsync(
                message,
                attempt,
                processEvent,
                stoppingToken);

            switch (outcome.Result)
            {
                case TelemetryMessageProcessingResult.ProcessedAndCommit:
                    return CoordinatorResult.Commit;

                case TelemetryMessageProcessingResult.RetryWithoutCommit:
                    var delay = KafkaProcessingRetryBackoff.ComputeDelay(
                        attempt,
                        _kafkaOptions.RetryInitialDelayMilliseconds,
                        _kafkaOptions.RetryMaxDelayMilliseconds);

                    _logger.LogInformation(
                        "Retrying same Kafka offset after backoff. Attempt={Attempt} DelayMs={DelayMs} Topic={Topic} Partition={Partition} Offset={Offset} Group={Group}",
                        attempt,
                        delay.TotalMilliseconds,
                        message.Topic,
                        message.Partition,
                        message.Offset,
                        _kafkaOptions.ConsumerGroup);

                    await Task.Delay(delay, stoppingToken);
                    break;

                case TelemetryMessageProcessingResult.RequiresDeadLetterPublish:
                    var pendingDeadLetter = outcome.PendingDeadLetter
                        ?? throw new InvalidOperationException("Se requiere DeadLetterMessage pendiente para publicar en DLQ.");
                    return await PublishDeadLetterUntilSuccessOrStopAsync(
                        message,
                        pendingDeadLetter,
                        stoppingToken);

                default:
                    throw new InvalidOperationException($"Unexpected processing result: {outcome.Result}");
            }
        }

        return CoordinatorResult.CancelledWithoutCommit;
    }

    private async Task<CoordinatorResult> PublishDeadLetterUntilSuccessOrStopAsync(
        KafkaConsumedMessage message,
        DeadLetterMessage pendingDeadLetter,
        CancellationToken stoppingToken)
    {
        try
        {
            return await PublishDeadLetterCoreAsync(message, pendingDeadLetter, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return CoordinatorResult.CancelledWithoutCommit;
        }
    }

    private async Task<CoordinatorResult> PublishDeadLetterCoreAsync(
        KafkaConsumedMessage message,
        DeadLetterMessage pendingDeadLetter,
        CancellationToken stoppingToken)
    {
        var session = new DeadLetterPublishRetrySession(
            _kafkaOptions.MaxDeadLetterPublishAttempts,
            _kafkaOptions.RetryInitialDelayMilliseconds,
            _kafkaOptions.RetryMaxDelayMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _messageProcessor.PublishDeadLetterAsync(pendingDeadLetter, stoppingToken);
                return CoordinatorResult.Commit;
            }
            catch (DeadLetterPublishException ex)
            {
                var decision = session.RegisterPublishFailure(
                    ex,
                    message.Topic,
                    message.Partition,
                    message.Offset);

                if (decision.ShouldStopWorker)
                {
                    _logger.LogCritical(
                        ex,
                        "Dead-letter publish failed repeatedly; stopping worker without commit. Topic={Topic} Partition={Partition} Offset={Offset} Attempt={Attempt} MaxAttempts={MaxAttempts}",
                        message.Topic,
                        message.Partition,
                        message.Offset,
                        decision.Attempt,
                        _kafkaOptions.MaxDeadLetterPublishAttempts);
                    _lifetime.StopApplication();
                    return CoordinatorResult.StopWithoutCommit;
                }

                _logger.LogError(
                    ex,
                    "Dead-letter publish failed; retrying without reprocessing message. Topic={Topic} Partition={Partition} Offset={Offset} Attempt={Attempt}",
                    message.Topic,
                    message.Partition,
                    message.Offset,
                    decision.Attempt);

                await Task.Delay(decision.Delay, stoppingToken);
            }
        }

        return CoordinatorResult.CancelledWithoutCommit;
    }
}

public enum CoordinatorResult
{
    Commit,
    StopWithoutCommit,
    CancelledWithoutCommit
}
