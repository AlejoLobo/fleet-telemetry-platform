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
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TelemetryConsumerWorker> _logger;

    public TelemetryConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        ResiliencePipelineFactory resilience,
        TelemetryMessageProcessor messageProcessor,
        IHostApplicationLifetime lifetime,
        ILogger<TelemetryConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _resilience = resilience;
        _messageProcessor = messageProcessor;
        _lifetime = lifetime;
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
            EnableAutoCommit = false,
            MaxPollIntervalMs = _kafkaOptions.MaxPollIntervalMilliseconds
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_kafkaOptions.TelemetryTopic);

        _logger.LogInformation(
            "Telemetry consumer started. Topic={Topic} DeadLetterTopic={DeadLetterTopic} Group={Group} MaxProcessingAttempts={MaxProcessingAttempts} MaxDeadLetterPublishAttempts={MaxDeadLetterPublishAttempts} MaxPollIntervalMs={MaxPollIntervalMs} RetryInitialDelayMs={RetryInitialDelayMs} RetryMaxDelayMs={RetryMaxDelayMs}",
            _kafkaOptions.TelemetryTopic,
            _kafkaOptions.DeadLetterTopic,
            _kafkaOptions.ConsumerGroup,
            _kafkaOptions.MaxProcessingAttempts,
            _kafkaOptions.MaxDeadLetterPublishAttempts,
            _kafkaOptions.MaxPollIntervalMilliseconds,
            _kafkaOptions.RetryInitialDelayMilliseconds,
            _kafkaOptions.RetryMaxDelayMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? consumeResult;

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

            // Solo omitir cuando no hay resultado o Message; Value null se normaliza a vacío → DLQ.
            if (consumeResult is null || consumeResult.Message is null)
                continue;

            var message = new KafkaConsumedMessage(
                Payload: consumeResult.Message.Value ?? string.Empty,
                Topic: consumeResult.Topic,
                Partition: consumeResult.Partition.Value,
                Offset: consumeResult.Offset.Value,
                Key: consumeResult.Message.Key);

            var session = new DeadLetterPublishRetrySession(
                _kafkaOptions.MaxDeadLetterPublishAttempts,
                _kafkaOptions.RetryInitialDelayMilliseconds,
                _kafkaOptions.RetryMaxDelayMilliseconds);

            var resolved = false;
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
                    var decision = session.RegisterFailure(
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
                        return;
                    }

                    _logger.LogError(
                        ex,
                        "DLQ or unexpected processing error; offset not committed. Topic={Topic} Partition={Partition} Offset={Offset} Attempt={Attempt}",
                        message.Topic,
                        message.Partition,
                        message.Offset,
                        decision.Attempt);

                    try
                    {
                        await Task.Delay(decision.Delay, stoppingToken);
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

                default:
                    throw new InvalidOperationException($"Unexpected processing result: {result}");
            }
        }

        return false;
    }
}
