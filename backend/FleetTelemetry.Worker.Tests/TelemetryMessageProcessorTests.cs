using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Worker.Tests;

public class TelemetryMessageProcessorTests
{
    private const string Topic = "telemetry.raw";

    [Fact]
    public async Task Invalid_json_publishes_dlq_with_invalid_payload_and_commit_result()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var message = new KafkaConsumedMessage("{ not valid json }", Topic, 0, 10);

        var processCalled = false;
        var result = await processor.ProcessAsync(
            message,
            (_, _) =>
            {
                processCalled = true;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.SentToDeadLetterAndCommit, result);
        Assert.False(processCalled);
        Assert.Single(dlq.Messages);
        Assert.Equal("invalid_payload", dlq.Messages[0].Reason);
        Assert.Equal(message.Payload, dlq.Messages[0].OriginalPayload);
        Assert.Equal(Topic, dlq.Messages[0].OriginalTopic);
        Assert.Equal(0, dlq.Messages[0].Partition);
        Assert.Equal(10, dlq.Messages[0].Offset);
    }

    [Fact]
    public async Task Partial_json_fails_domain_validation_and_publishes_dlq_invalid_payload()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq);
        var message = new KafkaConsumedMessage("""{"vehicleId":"VH-001"}""", Topic, 1, 22);

        var processCalled = false;
        var result = await processor.ProcessAsync(
            message,
            (_, _) =>
            {
                processCalled = true;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.SentToDeadLetterAndCommit, result);
        Assert.False(processCalled);
        Assert.Single(dlq.Messages);
        Assert.Equal("invalid_payload", dlq.Messages[0].Reason);
        Assert.Contains("EventId", dlq.Messages[0].ExceptionMessage);
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
        var result = await processor.ProcessAsync(
            message,
            (evt, _) =>
            {
                processed = evt;
                return Task.FromResult(ProcessTelemetryOutcome.Processed);
            });

        Assert.Equal(TelemetryMessageProcessingResult.ProcessedAndCommit, result);
        Assert.Empty(dlq.Messages);
        Assert.NotNull(processed);
        Assert.Equal(telemetryEvent.EventId, processed!.EventId);
        Assert.Equal(telemetryEvent.VehicleId, processed.VehicleId);
    }

    [Fact]
    public async Task Transient_db_error_does_not_publish_dlq_and_retries_without_commit()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 7);

        var result = await processor.ProcessAsync(
            message,
            (_, _) => throw new TimeoutException("database timeout"));

        Assert.Equal(TelemetryMessageProcessingResult.RetryWithoutCommit, result);
        Assert.Empty(dlq.Messages);
    }

    [Fact]
    public async Task Persistent_error_after_max_attempts_publishes_processing_failure_and_commits()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 2, 99);

        TelemetryMessageProcessingResult result = TelemetryMessageProcessingResult.IgnoreWithoutCommit;
        for (var i = 0; i < 3; i++)
        {
            result = await processor.ProcessAsync(
                message,
                (_, _) => throw new InvalidOperationException("persistent failure"));
        }

        Assert.Equal(TelemetryMessageProcessingResult.SentToDeadLetterAndCommit, result);
        Assert.Single(dlq.Messages);
        Assert.Equal("processing_failure", dlq.Messages[0].Reason);
        Assert.Contains("persistent failure", dlq.Messages[0].ExceptionMessage);
    }

    [Fact]
    public async Task Persistent_error_before_max_attempts_retries_without_dlq()
    {
        var dlq = new FakeDeadLetterPublisher();
        var processor = CreateProcessor(dlq, maxAttempts: 3);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 2, 100);

        var first = await processor.ProcessAsync(
            message,
            (_, _) => throw new InvalidOperationException("not yet"));
        var second = await processor.ProcessAsync(
            message,
            (_, _) => throw new InvalidOperationException("not yet"));

        Assert.Equal(TelemetryMessageProcessingResult.RetryWithoutCommit, first);
        Assert.Equal(TelemetryMessageProcessingResult.RetryWithoutCommit, second);
        Assert.Empty(dlq.Messages);
    }

    [Fact]
    public async Task Dlq_publish_failure_propagates_so_worker_does_not_commit()
    {
        var dlq = new FakeDeadLetterPublisher { ThrowOnPublish = true };
        var processor = CreateProcessor(dlq);
        var message = new KafkaConsumedMessage("{ bad", Topic, 0, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(
                message,
                (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed)));
    }

    private static TelemetryMessageProcessor CreateProcessor(
        FakeDeadLetterPublisher dlq,
        int maxAttempts = 3)
    {
        var options = Options.Create(new KafkaOptions
        {
            TelemetryTopic = Topic,
            DeadLetterTopic = "telemetry.dead-letter",
            MaxProcessingAttempts = maxAttempts
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
                throw new InvalidOperationException("DLQ publish failed");

            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
