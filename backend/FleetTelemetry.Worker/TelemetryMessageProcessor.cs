using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        if (currentAttempt < 1)
            throw new ArgumentOutOfRangeException(nameof(currentAttempt), currentAttempt, "currentAttempt debe ser >= 1.");

        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            await PublishDeadLetterAsync(
                message,
                reason: "invalid_payload",
                exceptionMessage: "Payload nulo, vacío o whitespace.",
                cancellationToken);
            return TelemetryMessageProcessingResult.SentToDeadLetterAndCommit;
        }

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
            else
            {
                _logger.LogInformation(
                    "Telemetry event duplicate skipped. EventId={EventId} Partition={Partition} Offset={Offset} Attempt={Attempt}",
                    telemetryEvent.EventId,
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
        catch (BrokenCircuitException ex)
        {
            return await HandleTransientFailureAsync(message, currentAttempt, ex, "circuit_breaker_open", cancellationToken);
        }
        catch (Exception ex) when (DatabaseTransientFailureClassifier.IsTransient(ex))
        {
            return await HandleTransientFailureAsync(message, currentAttempt, ex, "transient_database", cancellationToken);
        }
        catch (Exception ex)
        {
            // Error permanente: DLQ inmediata sin agotar reintentos.
            _logger.LogWarning(
                ex,
                "Permanent processing error; sending to DLQ immediately. Attempt={Attempt} Topic={Topic} Partition={Partition} Offset={Offset}",
                currentAttempt,
                message.Topic,
                message.Partition,
                message.Offset);

            await PublishDeadLetterAsync(
                message,
                reason: "processing_failure",
                exceptionMessage: ex.Message,
                cancellationToken);
            return TelemetryMessageProcessingResult.SentToDeadLetterAndCommit;
        }
    }

    private async Task<TelemetryMessageProcessingResult> HandleTransientFailureAsync(
        KafkaConsumedMessage message,
        int currentAttempt,
        Exception ex,
        string failureKind,
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

        _logger.LogWarning(
            ex,
            "Transient processing failure; retrying same offset. FailureKind={FailureKind} Attempt={Attempt} MaxAttempts={MaxAttempts} Topic={Topic} Partition={Partition} Offset={Offset}",
            failureKind,
            currentAttempt,
            _kafkaOptions.MaxProcessingAttempts,
            message.Topic,
            message.Partition,
            message.Offset);

        return TelemetryMessageProcessingResult.RetryWithoutCommit;
    }

    private async Task PublishDeadLetterAsync(
        KafkaConsumedMessage message,
        string reason,
        string exceptionMessage,
        CancellationToken cancellationToken)
    {
        var deadLetterMessage = new DeadLetterMessage(
            OriginalPayload: message.Payload ?? string.Empty,
            Reason: reason,
            ExceptionMessage: exceptionMessage,
            OriginalTopic: message.Topic,
            Partition: message.Partition,
            Offset: message.Offset,
            OccurredAt: DateTimeOffset.UtcNow);

        await _deadLetterPublisher.PublishAsync(deadLetterMessage, cancellationToken);

        _logger.LogWarning(
            "Message moved to dead letter queue. Reason={Reason} OriginalTopic={OriginalTopic} Partition={Partition} Offset={Offset} DeadLetterTopic={DeadLetterTopic}",
            reason,
            message.Topic,
            message.Partition,
            message.Offset,
            _kafkaOptions.DeadLetterTopic);
    }
}
