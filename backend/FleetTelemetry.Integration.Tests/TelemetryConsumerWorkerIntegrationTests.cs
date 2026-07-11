using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

[Collection(KafkaIntegrationCollection.Name)]
public class TelemetryConsumerWorkerIntegrationTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public TelemetryConsumerWorkerIntegrationTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task Failed_first_offset_is_retried_before_second_offset_is_processed()
    {
        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "worker-order",
            "worker-order-group");

        var eventA = CreateEvent("A");
        var eventB = CreateEvent("B");
        var attemptsByVehicle = new ConcurrentQueue<string>();
        var attemptCounts = new ConcurrentDictionary<string, int>();

        host.Processing!.Handler = async (evt, _) =>
        {
            var attempt = attemptCounts.AddOrUpdate(evt.VehicleId, 1, (_, current) => current + 1);
            attemptsByVehicle.Enqueue($"{evt.VehicleId}:{attempt}");
            if (evt.VehicleId == "A" && attempt == 1)
                throw new TimeoutException("transient A");
            return ProcessTelemetryOutcome.Processed;
        };

        await host.StartAsync();
        Produce(host.Topic, TelemetryEventJsonSerializer.Serialize(eventA));
        Produce(host.Topic, TelemetryEventJsonSerializer.Serialize(eventB));

        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, expectedOffset: 2, TimeSpan.FromSeconds(60));

        Assert.Equal(["A:1", "A:2", "B:1"], attemptsByVehicle.ToArray());

        await host.StopAsync();

        using var verifier = CreateConsumer(host.GroupId, host.Topic);
        var leftover = ConsumeOne(verifier, TimeSpan.FromSeconds(3));
        Assert.Null(leftover);
    }

    [Fact]
    public async Task Uncommitted_message_is_redelivered_after_worker_restart()
    {
        var topic = _kafka.NewTopicName("worker-redeliver");
        var dlqTopic = _kafka.NewTopicName("worker-redeliver-dlq");
        var group = _kafka.NewGroupId("worker-redeliver-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            Produce(topic, "{ bad-json");

            await using (var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "worker-redeliver",
                "worker-redeliver-group",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group,
                    ConfigureDeadLetterPublisher = dlq => dlq.FailUntilAttempt(int.MaxValue)
                }))
            {
                await host.StartAsync();
                await WaitUntilDlqAttemptsAsync(host, expectedAttempts: 3, TimeSpan.FromSeconds(45));
                await host.StopAsync();
                await KafkaTestPolling.WaitUntilConsumerGroupEmptyAsync(
                    _kafka.BootstrapServers,
                    group,
                    TimeSpan.FromSeconds(30));
            }

            var committedBefore = await GetCommittedOffsetAsync(group, topic);
            AssertNoCommittedOffset(committedBefore);

            await using (var host2 = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "worker-redeliver-2",
                "worker-redeliver-group-2",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group
                }))
            {
                await host2.StartAsync();
                await WaitUntilAsync(() => host2.DeadLetterPublisher!.Messages.Count > 0, TimeSpan.FromSeconds(60));
                await WaitUntilCommittedOffsetAsync(group, topic, expectedOffset: 1, TimeSpan.FromSeconds(30));
            }

            using var verifier = CreateConsumer(group, topic);
            Assert.Null(ConsumeOne(verifier, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(topic, dlqTopic);
        }
    }

    [Fact]
    public async Task Invalid_json_is_published_to_real_dlq_topic_before_commit()
    {
        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "worker-invalid-prod",
            "worker-invalid-prod-group",
            new TelemetryConsumerWorkerHostOptions
            {
                UseProductionDeadLetterPublisher = true
            });

        const string payload = "{ not-json";

        await host.StartAsync();
        Produce(host.Topic, payload);

        var dlqResult = await KafkaDlqTestHelper.WaitUntilDlqMessageAsync(
            _kafka.BootstrapServers,
            host.DeadLetterTopic,
            TimeSpan.FromSeconds(30));

        AssertDlqMessage(dlqResult, host.Topic, payload, "invalid_payload");
        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, expectedOffset: 1, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Whitespace_payload_is_published_to_real_dlq_topic_before_commit()
    {
        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "worker-empty-prod",
            "worker-empty-prod-group",
            new TelemetryConsumerWorkerHostOptions
            {
                UseProductionDeadLetterPublisher = true
            });

        const string payload = "\t";

        await host.StartAsync();
        Produce(host.Topic, payload);

        var dlqResult = await KafkaDlqTestHelper.WaitUntilDlqMessageAsync(
            _kafka.BootstrapServers,
            host.DeadLetterTopic,
            TimeSpan.FromSeconds(30));

        AssertDlqMessage(dlqResult, host.Topic, payload, "invalid_payload");
        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, expectedOffset: 1, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Permanent_error_goes_to_dlq_immediately()
    {
        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "worker-perm",
            "worker-perm-group");

        host.Processing!.Handler = (_, _) => throw new InvalidOperationException("permanent");

        await host.StartAsync();
        Produce(host.Topic, TelemetryEventJsonSerializer.Serialize(CreateEvent("P")));

        await WaitUntilAsync(() => host.Processing!.CallCount > 0, TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => host.DeadLetterPublisher!.Messages.Count > 0, TimeSpan.FromSeconds(15));
        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, expectedOffset: 1, TimeSpan.FromSeconds(15));

        Assert.Single(host.DeadLetterPublisher!.Messages);
        Assert.Equal("processing_failure", host.DeadLetterPublisher.Messages[0].Reason);
        Assert.Equal(1, host.Processing.CallCount);
    }

    [Fact]
    public async Task Dlq_unavailable_does_not_commit_offset()
    {
        var topic = _kafka.NewTopicName("worker-dlq-down");
        var dlqTopic = _kafka.NewTopicName("worker-dlq-down-dlq");
        var group = _kafka.NewGroupId("worker-dlq-down-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            Produce(topic, "{ bad");

            await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "worker-dlq-down",
                "worker-dlq-down-group",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group,
                    ConfigureDeadLetterPublisher = dlq => dlq.FailUntilAttempt(int.MaxValue)
                });

            await host.StartAsync();
            await WaitUntilDlqAttemptsAsync(host, expectedAttempts: 3, TimeSpan.FromSeconds(45));

            var committed = await GetCommittedOffsetAsync(group, topic);
            AssertNoCommittedOffset(committed);
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(topic, dlqTopic);
        }
    }

    [Fact]
    public async Task Cancellation_during_backoff_ends_cleanly()
    {
        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "worker-cancel",
            "worker-cancel-group",
            new TelemetryConsumerWorkerHostOptions
            {
                ConfigureKafka = options =>
                {
                    options.RetryInitialDelayMilliseconds = 5_000;
                    options.RetryMaxDelayMilliseconds = 5_000;
                    options.MaxProcessingAttempts = 5;
                }
            });

        host.Processing!.Handler = (_, _) => throw new TimeoutException("transient");

        await host.StartAsync();
        Produce(host.Topic, TelemetryEventJsonSerializer.Serialize(CreateEvent("C")));
        await WaitUntilAsync(() => host.Processing!.CallCount > 0, TimeSpan.FromSeconds(30));
        await host.StopAsync();

        var committed = await GetCommittedOffsetAsync(host.GroupId, host.Topic);
        AssertNoCommittedOffset(committed);
    }

    [Fact]
    public async Task Duplicate_event_id_leaves_single_db_row_and_commits_both_offsets()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "worker-dup",
            "worker-dup-group",
            new TelemetryConsumerWorkerHostOptions
            {
                ConnectionString = database.ConnectionString,
                UseRealTimescaleProcessing = true
            });

        var eventId = Guid.NewGuid();
        var payload = TelemetryEventJsonSerializer.Serialize(CreateEventWithId("VH-DUP", eventId));

        await host.StartAsync();
        Produce(host.Topic, payload);
        Produce(host.Topic, payload);

        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, expectedOffset: 2, TimeSpan.FromSeconds(60));

        var services = new ServiceCollection();
        services.AddDbContext<FleetDbContext>(o => o.UseNpgsql(database.ConnectionString));
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.TelemetryEvents.CountAsync(e => e.EventId == eventId));
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

    private static void AssertDlqMessage(
        ConsumeResult<string, string> dlqResult,
        string originalTopic,
        string originalPayload,
        string expectedReason)
    {
        Assert.Equal($"{originalTopic}:0:0", dlqResult.Message.Key);

        var json = KafkaDlqTestHelper.ParseDlqPayload(dlqResult.Message.Value!);
        Assert.Equal(expectedReason, json.GetProperty("reason").GetString());
        Assert.Equal(originalTopic, json.GetProperty("originalTopic").GetString());
        Assert.Equal(0, json.GetProperty("partition").GetInt32());
        Assert.Equal(0, json.GetProperty("offset").GetInt64());
        Assert.Equal(originalPayload, json.GetProperty("originalPayload").GetString());
    }

    private static void AssertNoCommittedOffset(long? committed)
    {
        Assert.True(committed is null or < 0, $"Expected no committed offset, got {committed}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(200);
        }

        Assert.True(condition(), "Condition was not met before timeout.");
    }

    private async Task WaitUntilCommittedOffsetAsync(string groupId, string topic, long expectedOffset, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var committed = await GetCommittedOffsetAsync(groupId, topic);
            if (committed == expectedOffset)
                return;

            await Task.Delay(200);
        }

        var final = await GetCommittedOffsetAsync(groupId, topic);
        Assert.Equal(expectedOffset, final);
    }

    private async Task WaitUntilDlqAttemptsAsync(
        TelemetryConsumerWorkerTestHost host,
        int expectedAttempts,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (host.DeadLetterPublisher!.PublishAttempts >= expectedAttempts)
                return;

            await Task.Delay(200);
        }

        Assert.True(host.DeadLetterPublisher!.PublishAttempts >= expectedAttempts);
    }

    private async Task<long?> GetCommittedOffsetAsync(string groupId, string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            SocketTimeoutMs = 5_000
        }).Build();

        try
        {
            var offsets = await admin.ListConsumerGroupOffsetsAsync(
                [
                    new ConsumerGroupTopicPartitions(
                        groupId,
                        [new TopicPartition(topic, 0)])
                ]);

            var groupResult = offsets.FirstOrDefault();
            var partition = groupResult?.Partitions.FirstOrDefault();
            if (partition is null || partition.Error.IsError)
                return null;

            var value = partition.Offset.Value;
            return value < 0 ? null : value;
        }
        catch (KafkaException)
        {
            return null;
        }
    }

    private static TelemetryEvent CreateEvent(string vehicleId) =>
        TelemetryEvent.Create(
            Guid.NewGuid(),
            vehicleId,
            "DRV-1",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            40,
            50,
            80);

    private static TelemetryEvent CreateEventWithId(string vehicleId, Guid eventId) =>
        TelemetryEvent.Create(
            eventId,
            vehicleId,
            "DRV-1",
            DateTimeOffset.UtcNow,
            4.65,
            -74.08,
            40,
            50,
            80);
}
