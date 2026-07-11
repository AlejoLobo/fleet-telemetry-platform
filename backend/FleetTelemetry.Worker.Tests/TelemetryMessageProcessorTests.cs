using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Worker.Tests;

public class TelemetryMessageProcessorTests
{
    private const string Topic = "telemetry.raw";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n  \t")]
    public async Task Whitespace_or_empty_payload_goes_to_dlq_invalid_payload(string? payload)
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq);
        var message = new KafkaConsumedMessage(payload!, Topic, 0, 1);

        var processCalled = false;
        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) =>
            {
                processCalled = true;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.RequiresDeadLetterPublish, outcome.Result);
        Assert.NotNull(outcome.PendingDeadLetter);
        Assert.False(processCalled);
        Assert.Empty(dlq.Messages);
        Assert.Equal("invalid_payload", outcome.PendingDeadLetter!.Reason);
    }

    [Fact]
    public async Task Json_literal_null_goes_to_dlq_invalid_payload()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq);
        var message = new KafkaConsumedMessage("null", Topic, 0, 2);

        var processCalled = false;
        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) =>
            {
                processCalled = true;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.RequiresDeadLetterPublish, outcome.Result);
        Assert.False(processCalled);
        Assert.Equal("invalid_payload", outcome.PendingDeadLetter!.Reason);
    }

    [Fact]
    public async Task Invalid_json_publishes_dlq_with_invalid_payload_and_commit_result()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var message = new KafkaConsumedMessage("{ not valid json }", Topic, 0, 10);

        var processCalled = false;
        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) =>
            {
                processCalled = true;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.RequiresDeadLetterPublish, outcome.Result);
        Assert.False(processCalled);
        Assert.NotNull(outcome.PendingDeadLetter);
        Assert.Equal("invalid_payload", outcome.PendingDeadLetter!.Reason);
        Assert.Equal(message.Payload, outcome.PendingDeadLetter.OriginalPayload);
        Assert.Equal(Topic, outcome.PendingDeadLetter.OriginalTopic);
        Assert.Equal(0, outcome.PendingDeadLetter.Partition);
        Assert.Equal(10, outcome.PendingDeadLetter.Offset);
    }

    [Fact]
    public async Task Partial_json_fails_domain_validation_and_publishes_dlq_invalid_payload()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq);
        var message = new KafkaConsumedMessage("""{"vehicleId":"VH-001"}""", Topic, 1, 22);

        var processCalled = false;
        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) =>
            {
                processCalled = true;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.RequiresDeadLetterPublish, outcome.Result);
        Assert.False(processCalled);
        Assert.NotNull(outcome.PendingDeadLetter);
        Assert.Equal("invalid_payload", outcome.PendingDeadLetter!.Reason);
        Assert.Contains("EventId", outcome.PendingDeadLetter.ExceptionMessage);
    }

    [Fact]
    public async Task Valid_event_processes_without_dlq_and_commits()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq);
        var telemetryEvent = CreateValidEvent();
        var payload = TelemetryEventJsonSerializer.Serialize(telemetryEvent);
        var message = new KafkaConsumedMessage(payload, Topic, 0, 5);

        TelemetryEvent? processed = null;
        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (evt, _) =>
            {
                processed = evt;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.ProcessedAndCommit, outcome.Result);
        Assert.Null(outcome.PendingDeadLetter);
        Assert.Empty(dlq.Messages);
        Assert.NotNull(processed);
        Assert.Equal(telemetryEvent.EventId, processed!.EventId);
    }

    [Fact]
    public async Task Duplicate_outcome_commits_without_dlq()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 6);

        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) => Task.FromResult(ProcessTelemetryOutcome.Duplicate));

        Assert.Equal(TelemetryMessageProcessingResult.ProcessedAndCommit, outcome.Result);
        Assert.Null(outcome.PendingDeadLetter);
        Assert.Empty(dlq.Messages);
    }

    [Fact]
    public async Task Transient_db_error_before_max_attempts_retries_without_dlq()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 7);

        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) => throw new TimeoutException("database timeout"));

        Assert.Equal(TelemetryMessageProcessingResult.RetryWithoutCommit, outcome.Result);
        Assert.Null(outcome.PendingDeadLetter);
        Assert.Empty(dlq.Messages);
    }

    [Fact]
    public async Task Transient_error_at_max_attempt_publishes_processing_failure()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 8);

        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 3,
            (_, _) => throw new TimeoutException("database timeout"));

        Assert.Equal(TelemetryMessageProcessingResult.RequiresDeadLetterPublish, outcome.Result);
        Assert.Equal("processing_failure", outcome.PendingDeadLetter!.Reason);
    }

    [Fact]
    public async Task Permanent_error_on_first_attempt_goes_to_dlq_immediately()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 5);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 2, 99);

        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) => throw new InvalidOperationException("permanent failure"));

        Assert.Equal(TelemetryMessageProcessingResult.RequiresDeadLetterPublish, outcome.Result);
        Assert.NotNull(outcome.PendingDeadLetter);
        Assert.Equal("processing_failure", outcome.PendingDeadLetter!.Reason);
    }

    [Fact]
    public async Task Circuit_breaker_open_retries_before_max()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 11);

        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) => throw new BrokenCircuitException("open"));

        Assert.Equal(TelemetryMessageProcessingResult.RetryWithoutCommit, outcome.Result);
        Assert.Empty(dlq.Messages);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 12);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(
                message,
                currentAttempt: 1,
                (_, _) => throw new OperationCanceledException(cts.Token),
                cts.Token));
    }

    [Fact]
    public async Task Dlq_publish_failure_propagates_as_dead_letter_publish_exception()
    {
        var dlq = new FakeDeadLetterPublisher { ThrowOnPublish = true };
        var processor = CreateProcessor(dlq);
        var message = new KafkaConsumedMessage("{ bad", Topic, 0, 1);

        var outcome = await processor.ProcessAsync(
            message,
            currentAttempt: 1,
            (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed));

        Assert.NotNull(outcome.PendingDeadLetter);
        await Assert.ThrowsAsync<DeadLetterPublishException>(() =>
            processor.PublishDeadLetterAsync(outcome.PendingDeadLetter!));
    }

    private static TelemetryMessageProcessor CreateProcessor(
        FakeDeadLetterPublisher dlq,
        int maxAttempts = 3)
    {
        var options = Options.Create(new KafkaOptions
        {
            TelemetryTopic = Topic,
            DeadLetterTopic = "telemetry.dead-letter",
            MaxProcessingAttempts = maxAttempts,
            RetryInitialDelayMilliseconds = 500,
            RetryMaxDelayMilliseconds = 5000,
            MaxDeadLetterPublishAttempts = 5
        });

        return new TelemetryMessageProcessor(
            dlq,
            options,
            NullLogger<TelemetryMessageProcessor>.Instance);
    }

    private static TelemetryEvent CreateValidEvent() => new()
    {
        EventId = Guid.NewGuid(),
        VehicleId = "VH-WORKER-001",
        DriverId = "DRV-001",
        Timestamp = DateTimeOffset.UtcNow,
        Latitude = 4.65,
        Longitude = -74.08,
        SpeedKmh = 40,
        FuelLevelPercent = 70,
        BatteryPercent = 80
    };

    private sealed class FakeDeadLetterPublisher : IDeadLetterPublisher
    {
        public List<DeadLetterMessage> Messages { get; } = [];
        public bool ThrowOnPublish { get; set; }

        public Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            if (ThrowOnPublish)
                throw new DeadLetterPublishException("DLQ publish failed");

            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
