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

// Lógica testeable de deserialización, validación, procesamiento y DLQ (sin estado de reintentos).
public class TelemetryMessageProcessor
{
    private readonly IDeadLetterPublisher _deadLetterPublisher;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<TelemetryMessageProcessor> _logger;

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
        int currentAttempt,
        Func<TelemetryEvent, CancellationToken, Task<ProcessTelemetryOutcome>> processEvent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message.Payload))
            return TelemetryMessageProcessingResult.IgnoreWithoutCommit;

        if (currentAttempt < 1)
            throw new ArgumentOutOfRangeException(nameof(currentAttempt), currentAttempt, "currentAttempt debe ser >= 1.");

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

            if (outcome == ProcessTelemetryOutcome.Processed)
            {
                _logger.LogInformation(
                    "Telemetry event processed. EventId={EventId} VehicleId={VehicleId} Partition={Partition} Offset={Offset} Attempt={Attempt}",
                    telemetryEvent.EventId,
                    telemetryEvent.VehicleId,
                    message.Partition,
                    message.Offset,
                    currentAttempt);
            }

            return TelemetryMessageProcessingResult.ProcessedAndCommit;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRetryableProcessingFailure(ex))
        {
            return await HandleProcessingFailureAsync(message, currentAttempt, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            return await HandleProcessingFailureAsync(message, currentAttempt, ex, cancellationToken);
        }
    }

    private async Task<TelemetryMessageProcessingResult> HandleProcessingFailureAsync(
        KafkaConsumedMessage message,
        int currentAttempt,
        Exception ex,
        CancellationToken cancellationToken)
    {
        if (currentAttempt >= _kafkaOptions.MaxProcessingAttempts)
        {
            await PublishDeadLetterAsync(
                message,
                reason: "processing_failure",
                exceptionMessage: ex.Message,
                cancellationToken);
            return TelemetryMessageProcessingResult.SentToDeadLetterAndCommit;
        }

        if (ex is BrokenCircuitException)
        {
            _logger.LogWarning(
                ex,
                "TimescaleDB circuit breaker open; retrying same offset. Attempt={Attempt} MaxAttempts={MaxAttempts} Topic={Topic} Partition={Partition} Offset={Offset}",
                currentAttempt,
                _kafkaOptions.MaxProcessingAttempts,
                message.Topic,
                message.Partition,
                message.Offset);
        }
        else if (IsInfrastructureTransientFailure(ex))
        {
            _logger.LogWarning(
                ex,
                "Transient infrastructure failure; retrying same offset. Attempt={Attempt} MaxAttempts={MaxAttempts} Topic={Topic} Partition={Partition} Offset={Offset}",
                currentAttempt,
                _kafkaOptions.MaxProcessingAttempts,
                message.Topic,
                message.Partition,
                message.Offset);
        }
        else
        {
            _logger.LogWarning(
                ex,
                "Non-transient processing error; retry pending. Attempt={Attempt} MaxAttempts={MaxAttempts} Topic={Topic} Partition={Partition} Offset={Offset}",
                currentAttempt,
                _kafkaOptions.MaxProcessingAttempts,
                message.Topic,
                message.Partition,
                message.Offset);
        }

        return TelemetryMessageProcessingResult.RetryWithoutCommit;
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

    private static bool IsRetryableProcessingFailure(Exception ex) =>
        ex is BrokenCircuitException || IsInfrastructureTransientFailure(ex);

    private static bool IsInfrastructureTransientFailure(Exception ex) =>
        ex is NpgsqlException or DbUpdateException or TimeoutException;
}
