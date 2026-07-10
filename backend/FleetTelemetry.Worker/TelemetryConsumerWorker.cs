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

// Consume Kafka y reintenta el mismo offset hasta resultado terminal (at-least-once).
public class TelemetryConsumerWorker : BackgroundService
{
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
            "Telemetry consumer started. Topic={Topic} DeadLetterTopic={DeadLetterTopic} Group={Group} MaxProcessingAttempts={MaxProcessingAttempts} RetryInitialDelayMs={RetryInitialDelayMs} RetryMaxDelayMs={RetryMaxDelayMs}",
            _kafkaOptions.TelemetryTopic,
            _kafkaOptions.DeadLetterTopic,
            _kafkaOptions.ConsumerGroup,
            _kafkaOptions.MaxProcessingAttempts,
            _kafkaOptions.RetryInitialDelayMilliseconds,
            _kafkaOptions.RetryMaxDelayMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? consumeResult = null;

            try
            {
                consumeResult = consumer.Consume(stoppingToken);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error. Reason={Reason}", ex.Error.Reason);
                continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (consumeResult?.Message?.Value is null)
                continue;

            var message = new KafkaConsumedMessage(
                Payload: consumeResult.Message.Value,
                Topic: consumeResult.Topic,
                Partition: consumeResult.Partition.Value,
                Offset: consumeResult.Offset.Value,
                Key: consumeResult.Message.Key);

            // No llamar a Consume() de nuevo hasta resolver este offset (o apagado).
            var resolved = false;
            var unexpectedFailureAttempt = 0;
            while (!resolved && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var shouldCommit = await ProcessUntilTerminalAsync(message, stoppingToken);
                    if (shouldCommit)
                        consumer.Commit(consumeResult);
                    resolved = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // p. ej. fallo al publicar DLQ: no confirmar; reintentar el mismo mensaje.
                    unexpectedFailureAttempt++;
                    _logger.LogError(
                        ex,
                        "Processing/DLQ error; offset not committed; will retry same message. Topic={Topic} Partition={Partition} Offset={Offset} UnexpectedAttempt={UnexpectedAttempt}",
                        message.Topic,
                        message.Partition,
                        message.Offset,
                        unexpectedFailureAttempt);

                    var delay = KafkaProcessingRetryBackoff.ComputeDelay(
                        unexpectedFailureAttempt,
                        _kafkaOptions.RetryInitialDelayMilliseconds,
                        _kafkaOptions.RetryMaxDelayMilliseconds);

                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        consumer.Close();
        _logger.LogInformation("Telemetry consumer stopped.");
    }

    // Reintenta el mismo mensaje hasta commit, ignore o cancelación.
    private async Task<bool> ProcessUntilTerminalAsync(
        KafkaConsumedMessage message,
        CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;

            var result = await _messageProcessor.ProcessAsync(
                message,
                attempt,
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
                    return true;

                case TelemetryMessageProcessingResult.IgnoreWithoutCommit:
                    return false;

                case TelemetryMessageProcessingResult.RetryWithoutCommit:
                    var delay = KafkaProcessingRetryBackoff.ComputeDelay(
                        attempt,
                        _kafkaOptions.RetryInitialDelayMilliseconds,
                        _kafkaOptions.RetryMaxDelayMilliseconds);

                    _logger.LogInformation(
                        "Retrying same Kafka offset after backoff. Attempt={Attempt} DelayMs={DelayMs} Topic={Topic} Partition={Partition} Offset={Offset}",
                        attempt,
                        delay.TotalMilliseconds,
                        message.Topic,
                        message.Partition,
                        message.Offset);

                    await Task.Delay(delay, stoppingToken);
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected processing result: {result}");
            }
        }

        return false;
    }
}
