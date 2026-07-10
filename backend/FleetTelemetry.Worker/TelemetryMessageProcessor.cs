using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Worker;

// Lógica testeable de deserialización, validación, procesamiento y DLQ.
public class TelemetryMessageProcessor
{
    private readonly IDeadLetterPublisher _deadLetterPublisher;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<TelemetryMessageProcessor> _logger;
    private readonly Dictionary<string, int> _processingAttempts = new();

    public TelemetryMessageProcessor(
        IDeadLetterPublisher deadLetterPublisher,
        IOptions<KafkaOptions> kafkaOptions,
        ILogger<TelemetryMessageProcessor> logger)
    {
        _deadLetterPublisher = deadLetterPublisher;
        _kafkaOptions = kafkaOptions.Value;
        _logger = logger;
    }

    public async Task<TelemetryMessageProcessingResult> ProcessAsync(
        KafkaConsumedMessage message,
        Func<TelemetryEvent, CancellationToken, Task<ProcessTelemetryOutcome>> processEvent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message.Payload))
            return TelemetryMessageProcessingResult.IgnoreWithoutCommit;

        var messageKey = BuildMessageKey(message);

        TelemetryEvent telemetryEvent;
        try
        {
            telemetryEvent = TelemetryEventJsonSerializer.Deserialize(message.Payload);
            TelemetryDomainEventValidator.Validate(telemetryEvent);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            await PublishDeadLetterAsync(
                message,
                reason: "invalid_payload",
                exceptionMessage: ex.Message,
                cancellationToken);
            return TelemetryMessageProcessingResult.SentToDeadLetterAndCommit;
        }

        try
        {
            var outcome = await processEvent(telemetryEvent, cancellationToken);
            _processingAttempts.Remove(messageKey);

            if (outcome == ProcessTelemetryOutcome.Processed)
            {
                _logger.LogInformation(
                    "Telemetry event processed. EventId={EventId} VehicleId={VehicleId} Partition={Partition} Offset={Offset}",
                    telemetryEvent.EventId,
                    telemetryEvent.VehicleId,
                    message.Partition,
                    message.Offset);
            }

            return TelemetryMessageProcessingResult.ProcessedAndCommit;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(
                ex,
                "TimescaleDB circuit breaker open; offset not committed. Topic={Topic} Partition={Partition} Offset={Offset}",
                message.Topic,
                message.Partition,
                message.Offset);
            return TelemetryMessageProcessingResult.RetryWithoutCommit;
        }
        catch (Exception ex) when (IsInfrastructureTransientFailure(ex))
        {
            _logger.LogWarning(
                ex,
                "Transient infrastructure failure; offset not committed and message not sent to DLQ. Topic={Topic} Partition={Partition} Offset={Offset}",
                message.Topic,
                message.Partition,
                message.Offset);
            return TelemetryMessageProcessingResult.RetryWithoutCommit;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var attempts = _processingAttempts.TryGetValue(messageKey, out var count) ? count + 1 : 1;
            _processingAttempts[messageKey] = attempts;

            if (attempts >= _kafkaOptions.MaxProcessingAttempts)
            {
                await PublishDeadLetterAsync(
                    message,
                    reason: "processing_failure",
                    exceptionMessage: ex.Message,
                    cancellationToken);
                _processingAttempts.Remove(messageKey);
                return TelemetryMessageProcessingResult.SentToDeadLetterAndCommit;
            }

            _logger.LogWarning(
                ex,
                "Non-transient processing error; retry pending. MessageKey={MessageKey} Attempt={Attempt} MaxAttempts={MaxAttempts} Topic={Topic} Partition={Partition} Offset={Offset}",
                messageKey,
                attempts,
                _kafkaOptions.MaxProcessingAttempts,
                message.Topic,
                message.Partition,
                message.Offset);
            return TelemetryMessageProcessingResult.RetryWithoutCommit;
        }
    }

    private async Task PublishDeadLetterAsync(
        KafkaConsumedMessage message,
        string reason,
        string exceptionMessage,
        CancellationToken cancellationToken)
    {
        var deadLetterMessage = new DeadLetterMessage(
            OriginalPayload: message.Payload,
            Reason: reason,
            ExceptionMessage: exceptionMessage,
            OriginalTopic: message.Topic,
            Partition: message.Partition,
            Offset: message.Offset,
            OccurredAt: DateTimeOffset.UtcNow);

        // El Worker solo confirma offset si esta publicación no lanza.
        await _deadLetterPublisher.PublishAsync(deadLetterMessage, cancellationToken);

        _logger.LogWarning(
            "Message moved to dead letter queue. Reason={Reason} OriginalTopic={OriginalTopic} Partition={Partition} Offset={Offset} DeadLetterTopic={DeadLetterTopic}",
            reason,
            message.Topic,
            message.Partition,
            message.Offset,
            _kafkaOptions.DeadLetterTopic);
    }

    private static bool IsInfrastructureTransientFailure(Exception ex) =>
        ex is NpgsqlException or DbUpdateException or TimeoutException;

    private static string BuildMessageKey(KafkaConsumedMessage message) =>
        $"{message.Topic}:{message.Partition}:{message.Offset}";
}
