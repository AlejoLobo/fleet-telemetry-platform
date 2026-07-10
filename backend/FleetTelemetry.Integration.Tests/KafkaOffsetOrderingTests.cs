using System.Collections.Concurrent;
using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

[Collection(KafkaIntegrationCollection.Name)]
public class KafkaOffsetOrderingTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public KafkaOffsetOrderingTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task Failed_first_offset_is_retried_before_second_offset_is_processed()
    {
        var topic = _kafka.NewTopicName("order");
        var group = _kafka.NewGroupId("order-group");
        await _kafka.CreateTopicAsync(topic, partitions: 1);

        var eventA = CreateEvent("A");
        var eventB = CreateEvent("B");
        Produce(topic, TelemetryEventJsonSerializer.Serialize(eventA), key: "k");
        Produce(topic, TelemetryEventJsonSerializer.Serialize(eventB), key: "k");

        var attemptsByVehicle = new ConcurrentQueue<string>();
        var aAttempts = 0;

        var processor = CreateProcessor(
            new RecordingDeadLetterPublisher(),
            topic,
            maxAttempts: 3,
            retryInitialMs: 50,
            retryMaxMs: 200);

        using var consumer = CreateConsumer(group, topic);
        var processed = new List<string>();

        // Mensaje A
        var first = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
        Assert.NotNull(first);
        await ProcessUntilCommitAsync(
            consumer,
            first!,
            processor,
            async (evt, _) =>
            {
                attemptsByVehicle.Enqueue($"{evt.VehicleId}:{Interlocked.Increment(ref aAttempts)}");
                if (evt.VehicleId == "A" && aAttempts == 1)
                    throw new TimeoutException("transient A");
                processed.Add(evt.VehicleId);
                return ProcessTelemetryOutcome.Processed;
            });

        // Mensaje B — solo después de resolver A
        var second = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
        Assert.NotNull(second);
        await ProcessUntilCommitAsync(
            consumer,
            second!,
            processor,
            async (evt, _) =>
            {
                attemptsByVehicle.Enqueue($"{evt.VehicleId}:1");
                processed.Add(evt.VehicleId);
                return ProcessTelemetryOutcome.Processed;
            });

        Assert.Equal(["A", "B"], processed);
        Assert.Equal(["A:1", "A:2", "B:1"], attemptsByVehicle.ToArray());

        // Reinicio con el mismo group: no deben reaparecer A/B.
        consumer.Close();
        using var consumer2 = CreateConsumer(group, topic);
        var leftover = ConsumeOne(consumer2, TimeSpan.FromSeconds(5));
        Assert.Null(leftover);
    }

    [Fact]
    public async Task Uncommitted_message_is_redelivered_after_restart()
    {
        var topic = _kafka.NewTopicName("redeliver");
        var group = _kafka.NewGroupId("redeliver-group");
        await _kafka.CreateTopicAsync(topic, partitions: 1);

        var evt = CreateEvent("R");
        Produce(topic, TelemetryEventJsonSerializer.Serialize(evt));

        long firstOffset;
        using (var consumer = CreateConsumer(group, topic))
        {
            var result = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
            Assert.NotNull(result);
            firstOffset = result!.Offset.Value;
            // Sin commit a propósito.
            consumer.Close();
        }

        using var consumer2 = CreateConsumer(group, topic);
        var again = ConsumeOne(consumer2, TimeSpan.FromSeconds(30));
        Assert.NotNull(again);
        Assert.Equal(firstOffset, again!.Offset.Value);
        Assert.Contains("R", again.Message.Value);
    }

    [Fact]
    public async Task Invalid_json_goes_to_dlq_and_commits()
    {
        var topic = _kafka.NewTopicName("invalid");
        var dlqTopic = _kafka.NewTopicName("dlq");
        var group = _kafka.NewGroupId("invalid-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        Produce(topic, "{ not-json");

        var dlq = new KafkaRecordingDeadLetterPublisher(_kafka.BootstrapServers, dlqTopic);
        var processor = CreateProcessor(dlq, topic, deadLetterTopic: dlqTopic);

        using var consumer = CreateConsumer(group, topic);
        var result = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
        Assert.NotNull(result);

        await ProcessUntilCommitAsync(
            consumer,
            result!,
            processor,
            (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed));

        var dlqMsg = ConsumeDlq(dlqTopic, TimeSpan.FromSeconds(20));
        Assert.NotNull(dlqMsg);
        Assert.Contains("invalid_payload", dlqMsg!);
    }

    [Fact]
    public async Task Empty_payload_goes_to_dlq_and_commits()
    {
        var topic = _kafka.NewTopicName("empty");
        var dlqTopic = _kafka.NewTopicName("dlq-empty");
        var group = _kafka.NewGroupId("empty-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        Produce(topic, "   ");

        var dlq = new KafkaRecordingDeadLetterPublisher(_kafka.BootstrapServers, dlqTopic);
        var processor = CreateProcessor(dlq, topic, deadLetterTopic: dlqTopic);

        using var consumer = CreateConsumer(group, topic);
        var result = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
        Assert.NotNull(result);

        await ProcessUntilCommitAsync(
            consumer,
            result!,
            processor,
            (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed));

        var dlqMsg = ConsumeDlq(dlqTopic, TimeSpan.FromSeconds(20));
        Assert.NotNull(dlqMsg);
        Assert.Contains("invalid_payload", dlqMsg!);
    }

    [Fact]
    public async Task Permanent_error_goes_to_dlq_immediately()
    {
        var topic = _kafka.NewTopicName("perm");
        var dlqTopic = _kafka.NewTopicName("dlq-perm");
        var group = _kafka.NewGroupId("perm-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        Produce(topic, TelemetryEventJsonSerializer.Serialize(CreateEvent("P")));

        var dlq = new RecordingDeadLetterPublisher();
        var processor = CreateProcessor(dlq, topic, deadLetterTopic: dlqTopic, maxAttempts: 5);

        using var consumer = CreateConsumer(group, topic);
        var result = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
        Assert.NotNull(result);

        await ProcessUntilCommitAsync(
            consumer,
            result!,
            processor,
            (_, _) => throw new InvalidOperationException("permanent"));

        Assert.Single(dlq.Messages);
        Assert.Equal("processing_failure", dlq.Messages[0].Reason);
    }

    [Fact]
    public async Task Dlq_unavailable_does_not_commit_offset()
    {
        var topic = _kafka.NewTopicName("dlq-down");
        var group = _kafka.NewGroupId("dlq-down-group");
        await _kafka.CreateTopicAsync(topic);
        Produce(topic, "{ bad");

        var failingDlq = new FailingDeadLetterPublisher();
        var processor = CreateProcessor(failingDlq, topic);
        var session = new DeadLetterPublishRetrySession(3, 20, 50);

        using var consumer = CreateConsumer(group, topic);
        var result = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
        Assert.NotNull(result);

        var committed = false;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                await ProcessUntilCommitAsync(
                    consumer,
                    result!,
                    processor,
                    (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed));
                committed = true;
                break;
            }
            catch (Exception ex)
            {
                var decision = session.RegisterFailure(ex, topic, 0, result!.Offset.Value);
                if (decision.ShouldStopWorker)
                    break;
                await Task.Delay(decision.Delay);
            }
        }

        Assert.False(committed);

        consumer.Close();
        using var consumer2 = CreateConsumer(group, topic);
        var again = ConsumeOne(consumer2, TimeSpan.FromSeconds(30));
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Cancellation_during_backoff_ends_cleanly()
    {
        var topic = _kafka.NewTopicName("cancel");
        var group = _kafka.NewGroupId("cancel-group");
        await _kafka.CreateTopicAsync(topic);
        Produce(topic, TelemetryEventJsonSerializer.Serialize(CreateEvent("C")));

        var processor = CreateProcessor(new RecordingDeadLetterPublisher(), topic, maxAttempts: 5, retryInitialMs: 5_000, retryMaxMs: 5_000);
        using var consumer = CreateConsumer(group, topic);
        var result = ConsumeOne(consumer, TimeSpan.FromSeconds(30));
        Assert.NotNull(result);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await ProcessUntilCommitAsync(
                consumer,
                result!,
                processor,
                (_, _) => throw new TimeoutException("transient"),
                cts.Token);
        });
    }

    private void Produce(string topic, string payload, string? key = null)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            Acks = Acks.All
        }).Build();

        producer.Produce(topic, new Message<string, string> { Key = key ?? "k", Value = payload });
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    private IConsumer<string, string> CreateConsumer(string group, string topic)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = group,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            MaxPollIntervalMs = 300_000
        }).Build();
        consumer.Subscribe(topic);
        return consumer;
    }

    private static ConsumeResult<string, string>? ConsumeOne(IConsumer<string, string> consumer, TimeSpan timeout)
    {
        try
        {
            return consumer.Consume(timeout);
        }
        catch (ConsumeException)
        {
            return null;
        }
    }

    private string? ConsumeDlq(string dlqTopic, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = _kafka.NewGroupId("dlq-reader"),
            EnableAutoCommit = true,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(dlqTopic);
        var result = consumer.Consume(timeout);
        return result?.Message?.Value;
    }

    private static async Task ProcessUntilCommitAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> consumeResult,
        TelemetryMessageProcessor processor,
        Func<TelemetryEvent, CancellationToken, Task<ProcessTelemetryOutcome>> processEvent,
        CancellationToken cancellationToken = default)
    {
        var message = new KafkaConsumedMessage(
            consumeResult.Message.Value ?? string.Empty,
            consumeResult.Topic,
            consumeResult.Partition.Value,
            consumeResult.Offset.Value,
            consumeResult.Message.Key);

        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            var result = await processor.ProcessAsync(message, attempt, processEvent, cancellationToken);
            switch (result)
            {
                case TelemetryMessageProcessingResult.ProcessedAndCommit:
                case TelemetryMessageProcessingResult.SentToDeadLetterAndCommit:
                    consumer.Commit(consumeResult);
                    return;
                case TelemetryMessageProcessingResult.RetryWithoutCommit:
                    await Task.Delay(
                        KafkaProcessingRetryBackoff.ComputeDelay(attempt, 50, 200),
                        cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException(result.ToString());
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static TelemetryMessageProcessor CreateProcessor(
        IDeadLetterPublisher dlq,
        string topic,
        string? deadLetterTopic = null,
        int maxAttempts = 3,
        int retryInitialMs = 50,
        int retryMaxMs = 200)
    {
        var options = Options.Create(new KafkaOptions
        {
            TelemetryTopic = topic,
            DeadLetterTopic = deadLetterTopic ?? "telemetry.dead-letter",
            MaxProcessingAttempts = maxAttempts,
            RetryInitialDelayMilliseconds = retryInitialMs,
            RetryMaxDelayMilliseconds = retryMaxMs,
            MaxDeadLetterPublishAttempts = 5,
            MaxPollIntervalMilliseconds = 300_000
        });

        return new TelemetryMessageProcessor(dlq, options, NullLogger<TelemetryMessageProcessor>.Instance);
    }

    private static TelemetryEvent CreateEvent(string vehicleId) => new()
    {
        EventId = Guid.NewGuid(),
        VehicleId = vehicleId,
        DriverId = "DRV-1",
        Timestamp = DateTimeOffset.UtcNow,
        Latitude = 4.65,
        Longitude = -74.08,
        SpeedKmh = 40,
        FuelLevelPercent = 50,
        BatteryPercent = 80
    };

    private sealed class RecordingDeadLetterPublisher : IDeadLetterPublisher
    {
        public List<DeadLetterMessage> Messages { get; } = [];

        public Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingDeadLetterPublisher : IDeadLetterPublisher
    {
        public Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DLQ unavailable");
    }

    private sealed class KafkaRecordingDeadLetterPublisher : IDeadLetterPublisher
    {
        private readonly IProducer<string, string> _producer;
        private readonly string _topic;

        public KafkaRecordingDeadLetterPublisher(string bootstrap, string topic)
        {
            _topic = topic;
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrap,
                Acks = Acks.All
            }).Build();
        }

        public async Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                reason = message.Reason,
                originalTopic = message.OriginalTopic,
                partition = message.Partition,
                offset = message.Offset
            });
            await _producer.ProduceAsync(
                _topic,
                new Message<string, string> { Value = json },
                cancellationToken);
        }
    }
}
