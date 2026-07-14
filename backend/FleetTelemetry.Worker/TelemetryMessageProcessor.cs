using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Observability;
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
    private readonly FleetTelemetryMetrics _metrics;
    private readonly ILogger<TelemetryMessageProcessor> _logger;

    public TelemetryMessageProcessor(
        IDeadLetterPublisher deadLetterPublisher,
        IOptions<KafkaOptions> kafkaOptions,
        FleetTelemetryMetrics metrics,
        ILogger<TelemetryMessageProcessor> logger)
    {
        _deadLetterPublisher = deadLetterPublisher;
        _kafkaOptions = kafkaOptions.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<TelemetryMessageProcessingOutcome> ProcessAsync(
        KafkaConsumedMessage message,
        int currentAttempt,
        Func<TelemetryEvent, CancellationToken, Task<ProcessTelemetryOutcome>> processEvent,
        CancellationToken cancellationToken = default)
    {
        if (currentAttempt < 1)
            throw new ArgumentOutOfRangeException(nameof(currentAttempt), currentAttempt, "currentAttempt debe ser >= 1.");

        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            _metrics.TelemetryInvalidTotal.Add(1);
            return TerminalDeadLetterOutcome(
                message,
                reason: "null_payload",
                exceptionMessage: "Payload nulo, vacío o whitespace.");
        }

        TelemetryEvent telemetryEvent;
        try
        {
            telemetryEvent = TelemetryEventJsonSerializer.Deserialize(
                message.Payload,
                _kafkaOptions.UseEventEnvelope);
            TelemetryDomainEventValidator.Validate(telemetryEvent);
        }
        catch (TelemetryKafkaContractException ex)
        {
            _metrics.TelemetryInvalidTotal.Add(1);
            return TerminalDeadLetterOutcome(
                message,
                reason: ex.ErrorCode,
                exceptionMessage: ex.Message);
        }
        catch (ArgumentException ex)
        {
            _metrics.TelemetryInvalidTotal.Add(1);
            return TerminalDeadLetterOutcome(
                message,
                reason: "invalid_domain",
                exceptionMessage: ex.Message);
        }

        try
        {
            using var duration = _metrics.TrackProcessingDuration();
            var outcome = await processEvent(telemetryEvent, cancellationToken);

            if (outcome == ProcessTelemetryOutcome.Processed)
            {
                _metrics.TelemetryProcessedTotal.Add(1);
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
                _metrics.TelemetryDuplicateTotal.Add(1);
                _logger.LogInformation(
                    "Telemetry event duplicate skipped. EventId={EventId} Partition={Partition} Offset={Offset} Attempt={Attempt}",
                    telemetryEvent.EventId,
                    message.Partition,
                    message.Offset,
                    currentAttempt);
            }

            return new TelemetryMessageProcessingOutcome(TelemetryMessageProcessingResult.ProcessedAndCommit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokenCircuitException ex)
        {
            return await HandleTransientFailureAsync(message, currentAttempt, ex, "circuit_breaker_open");
        }
        catch (Exception ex) when (DatabaseTransientFailureClassifier.IsTransient(ex))
        {
            return await HandleTransientFailureAsync(message, currentAttempt, ex, "transient_database");
        }
        // Excepciones desconocidas se propagan: no DLQ ni commit (el coordinador detiene el Worker).
    }

    public async Task PublishDeadLetterAsync(DeadLetterMessage deadLetterMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            await _deadLetterPublisher.PublishAsync(deadLetterMessage, cancellationToken);

            _logger.LogWarning(
                "Message moved to dead letter queue. Reason={Reason} OriginalTopic={OriginalTopic} Partition={Partition} Offset={Offset} DeadLetterTopic={DeadLetterTopic}",
                deadLetterMessage.Reason,
                deadLetterMessage.OriginalTopic,
                deadLetterMessage.Partition,
                deadLetterMessage.Offset,
                _kafkaOptions.DeadLetterTopic);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DeadLetterPublishException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DeadLetterPublishException("Dead-letter publish failed.", ex);
        }
    }

    private Task<TelemetryMessageProcessingOutcome> HandleTransientFailureAsync(
        KafkaConsumedMessage message,
        int currentAttempt,
        Exception ex,
        string failureKind)
    {
        if (currentAttempt >= _kafkaOptions.MaxProcessingAttempts)
        {
            return Task.FromResult(TerminalDeadLetterOutcome(
                message,
                reason: "processing_failure",
                exceptionMessage: ex.Message,
                attemptNumber: currentAttempt));
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

        return Task.FromResult(
            new TelemetryMessageProcessingOutcome(TelemetryMessageProcessingResult.RetryWithoutCommit));
    }

    private TelemetryMessageProcessingOutcome TerminalDeadLetterOutcome(
        KafkaConsumedMessage message,
        string reason,
        string exceptionMessage,
        int attemptNumber = 1)
    {
        var deadLetterMessage = BuildDeadLetterMessage(message, reason, exceptionMessage, attemptNumber);
        return new TelemetryMessageProcessingOutcome(
            TelemetryMessageProcessingResult.RequiresDeadLetterPublish,
            deadLetterMessage);
    }

    private static DeadLetterMessage BuildDeadLetterMessage(
        KafkaConsumedMessage message,
        string reason,
        string exceptionMessage,
        int attemptNumber)
    {
        var category = reason switch
        {
            "invalid_json" or "null_payload" or "invalid_envelope" or "unknown_event_type" or "invalid_domain" => "validation",
            "unsupported_schema_version" => "contract",
            "invalid_payload" => "validation",
            "processing_failure" => "processing",
            _ => "unknown"
        };

        Guid? correlationId = null;
        try
        {
            var parsed = TelemetryEventJsonSerializer.Deserialize(
                message.Payload,
                useEventEnvelope: false);
            correlationId = parsed.EventId;
        }
        catch
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(message.Payload);
                if (doc.RootElement.TryGetProperty("eventId", out var eventIdProp)
                    && eventIdProp.ValueKind == System.Text.Json.JsonValueKind.String
                    && Guid.TryParse(eventIdProp.GetString(), out var envelopeEventId))
                {
                    correlationId = envelopeEventId;
                }
                else if (doc.RootElement.TryGetProperty("payload", out var payloadProp)
                         && payloadProp.TryGetProperty("eventId", out var nestedEventId)
                         && nestedEventId.ValueKind == System.Text.Json.JsonValueKind.String
                         && Guid.TryParse(nestedEventId.GetString(), out var payloadEventId))
                {
                    correlationId = payloadEventId;
                }
            }
            catch
            {
                // Payload inválido: no hay correlationId confiable.
            }
        }

        var sanitizedDetail = SanitizeTechnicalDetail(exceptionMessage);

        return new DeadLetterMessage(
            DeadLetterId: Guid.NewGuid(),
            SchemaVersion: 1,
            Category: category,
            ErrorCode: reason,
            AttemptNumber: attemptNumber,
            OccurredAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            OriginalTopic: message.Topic,
            Partition: message.Partition,
            Offset: message.Offset,
            MessageKey: message.Key,
            CorrelationId: correlationId,
            OriginalPayload: message.Payload ?? string.Empty,
            TechnicalDetail: sanitizedDetail,
            Reason: reason,
            ExceptionMessage: sanitizedDetail);
    }

    private static string SanitizeTechnicalDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "unknown";

        const int maxLength = 500;
        var normalized = detail.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
