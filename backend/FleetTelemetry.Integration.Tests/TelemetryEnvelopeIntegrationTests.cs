using System.Text.Json;
using Confluent.Kafka;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Integration.Tests;

[Collection(KafkaIntegrationCollection.Name)]
public class TelemetryEnvelopeIntegrationTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public TelemetryEnvelopeIntegrationTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task Envelope_v1_publisher_to_worker_persists_telemetry_with_matching_fields()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "envelope-e2e",
            "envelope-e2e-group",
            new TelemetryConsumerWorkerHostOptions
            {
                ConnectionString = database.ConnectionString,
                UseRealTimescaleProcessing = true,
                UseEventEnvelope = true
            });

        var original = CreateSampleEvent();
        var payload = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: true);

        await host.StartAsync();
        Produce(host.Topic, payload, original.VehicleId, _kafka.BootstrapServers);
        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, 1, TimeSpan.FromSeconds(60));

        using var scope = CreateDbScope(database.ConnectionString);
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var row = await db.TelemetryEvents.SingleAsync(e => e.EventId == original.EventId);

        Assert.Equal(original.VehicleId, row.VehicleId);
        Assert.Equal(original.DriverId, row.DriverId);
        Assert.Equal(original.Timestamp.UtcDateTime, row.Timestamp.UtcDateTime, TimeSpan.FromSeconds(1));
        Assert.Equal(original.Latitude, row.Latitude, precision: 4);
        Assert.Equal(original.Longitude, row.Longitude, precision: 4);
        Assert.Equal(original.SpeedKmh, row.SpeedKmh, precision: 2);
    }

    [Fact]
    public async Task Unsupported_schema_version_goes_to_dlq_without_persistence()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        var topic = _kafka.NewTopicName("envelope-unsupported");
        var dlqTopic = _kafka.NewTopicName("envelope-unsupported-dlq");
        var group = _kafka.NewGroupId("envelope-unsupported-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            var original = CreateSampleEvent();
            var envelopeJson = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: true)
                .Replace("\"schemaVersion\":1", "\"schemaVersion\":99", StringComparison.Ordinal);

            await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "envelope-unsupported-host",
                "envelope-unsupported-host-group",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group,
                    ConnectionString = database.ConnectionString,
                    UseRealTimescaleProcessing = true,
                    UseProductionDeadLetterPublisher = true,
                    UseEventEnvelope = true
                });

            await host.StartAsync();
            Produce(topic, envelopeJson, original.VehicleId, _kafka.BootstrapServers);
            await WaitUntilCommittedOffsetAsync(group, topic, 1, TimeSpan.FromSeconds(60));

            using var dlqConsumer = CreateDlqConsumer(group, dlqTopic, _kafka.BootstrapServers);
            var dlqMessage = ConsumeOne(dlqConsumer, TimeSpan.FromSeconds(30));
            Assert.NotNull(dlqMessage);

            var dlqJson = KafkaDlqTestHelper.ParseDlqPayload(dlqMessage!.Message.Value!);
            Assert.Equal("unsupported_schema_version", dlqJson.GetProperty("reason").GetString());
            Assert.Equal(topic, dlqJson.GetProperty("originalTopic").GetString());
            Assert.Equal(0, dlqJson.GetProperty("partition").GetInt32());
            Assert.Equal(0, dlqJson.GetProperty("offset").GetInt64());

            using var scope = CreateDbScope(database.ConnectionString);
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(0, await db.TelemetryEvents.CountAsync(e => e.EventId == original.EventId));
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(topic, dlqTopic);
        }
    }

    [Fact]
    public async Task Malformed_envelope_does_not_fallback_to_legacy_and_goes_to_dlq()
    {
        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "envelope-malformed",
            "envelope-malformed-group",
            new TelemetryConsumerWorkerHostOptions
            {
                UseEventEnvelope = true
            });

        const string malformedEnvelope = """
            {
              "schemaVersion": 1,
              "eventType": "fleet.telemetry.received",
              "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "occurredAt": "2026-07-12T08:30:00Z"
            }
            """;

        await host.StartAsync();
        Produce(host.Topic, malformedEnvelope, key: null, _kafka.BootstrapServers);
        await WaitUntilAsync(() => host.DeadLetterPublisher!.Messages.Count > 0, TimeSpan.FromSeconds(30));
        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, 1, TimeSpan.FromSeconds(30));

        var dlq = host.DeadLetterPublisher!.Messages.Single();
        Assert.Equal("invalid_envelope", dlq.Reason);
        Assert.Equal(0, host.Processing!.CallCount);
    }

    [Fact]
    public async Task Legacy_format_works_when_use_event_envelope_false()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "legacy-compat",
            "legacy-compat-group",
            new TelemetryConsumerWorkerHostOptions
            {
                ConnectionString = database.ConnectionString,
                UseRealTimescaleProcessing = true,
                UseEventEnvelope = false
            });

        var original = CreateSampleEvent();
        var payload = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: false);

        await host.StartAsync();
        Produce(host.Topic, payload, original.VehicleId, _kafka.BootstrapServers);
        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, 1, TimeSpan.FromSeconds(60));

        using var scope = CreateDbScope(database.ConnectionString);
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.TelemetryEvents.CountAsync(e => e.EventId == original.EventId));
    }

    [Fact]
    public async Task Duplicate_envelope_event_id_persists_single_row_and_commits_both_offsets()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "envelope-dup",
            "envelope-dup-group",
            new TelemetryConsumerWorkerHostOptions
            {
                ConnectionString = database.ConnectionString,
                UseRealTimescaleProcessing = true,
                UseEventEnvelope = true
            });

        var eventId = Guid.NewGuid();
        var original = CreateSampleEvent(eventId);
        var payload = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: true);

        await host.StartAsync();
        Produce(host.Topic, payload, original.VehicleId, _kafka.BootstrapServers);
        Produce(host.Topic, payload, original.VehicleId, _kafka.BootstrapServers);
        await WaitUntilCommittedOffsetAsync(host.GroupId, host.Topic, 2, TimeSpan.FromSeconds(60));

        using var scope = CreateDbScope(database.ConnectionString);
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        Assert.Equal(1, await db.TelemetryEvents.CountAsync(e => e.EventId == eventId));
    }

    [Fact]
    public async Task Dlq_publish_failure_does_not_commit_offset_for_envelope_message()
    {
        await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
            _kafka,
            "envelope-dlq-fail",
            "envelope-dlq-fail-group",
            new TelemetryConsumerWorkerHostOptions
            {
                UseEventEnvelope = true,
                ConfigureDeadLetterPublisher = dlq => dlq.FailUntilAttempt(int.MaxValue)
            });

        var unsupported = TelemetryEventJsonSerializer.Serialize(CreateSampleEvent(), useEnvelope: true)
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":42", StringComparison.Ordinal);

        await host.StartAsync();
        Produce(host.Topic, unsupported, key: null, _kafka.BootstrapServers);
        await WaitUntilAsync(() => host.DeadLetterPublisher!.PublishAttempts >= 3, TimeSpan.FromSeconds(45));
        await host.StopAsync();

        var committed = await GetCommittedOffsetAsync(host.GroupId, host.Topic);
        Assert.True(committed is null or < 0);
    }

    [Fact]
    public async Task Envelope_con_occurredAt_contradictorio_no_persiste_y_dlq_invalid_envelope()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        var topic = _kafka.NewTopicName("envelope-occurred-at");
        var dlqTopic = _kafka.NewTopicName("envelope-occurred-at-dlq");
        var group = _kafka.NewGroupId("envelope-occurred-at-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            var original = CreateSampleEvent();
            const string contradictoryEnvelope = """
                {
                  "schemaVersion": 1,
                  "eventType": "fleet.telemetry.received",
                  "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "occurredAt": "2026-07-12T08:30:00Z",
                  "payload": {
                    "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    "vehicleId": "VH-ENV",
                    "driverId": "DRV-ENV",
                    "timestamp": "2026-07-12T09:00:00Z",
                    "latitude": 4.71,
                    "longitude": -74.07,
                    "speedKmh": 55.5,
                    "fuelLevelPercent": 60.0,
                    "batteryPercent": 75.0
                  }
                }
                """;

            await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "envelope-occurred-at-host",
                "envelope-occurred-at-host-group",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group,
                    ConnectionString = database.ConnectionString,
                    UseRealTimescaleProcessing = true,
                    UseProductionDeadLetterPublisher = true,
                    UseEventEnvelope = true
                });

            await host.StartAsync();
            Produce(topic, contradictoryEnvelope, original.VehicleId, _kafka.BootstrapServers);
            await WaitUntilCommittedOffsetAsync(group, topic, 1, TimeSpan.FromSeconds(60));

            using var dlqConsumer = CreateDlqConsumer(group, dlqTopic, _kafka.BootstrapServers);
            var dlqMessage = ConsumeOne(dlqConsumer, TimeSpan.FromSeconds(30));
            Assert.NotNull(dlqMessage);

            var dlqJson = KafkaDlqTestHelper.ParseDlqPayload(dlqMessage!.Message.Value!);
            Assert.Equal("invalid_envelope", dlqJson.GetProperty("reason").GetString());

            using var scope = CreateDbScope(database.ConnectionString);
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            var contradictoryEventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            Assert.Equal(0, await db.TelemetryEvents.CountAsync(e => e.EventId == contradictoryEventId));
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(topic, dlqTopic);
        }
    }

    [Fact]
    public async Task Envelope_sin_metadata_obligatoria_no_persiste_y_dlq_invalid_envelope()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        var topic = _kafka.NewTopicName("envelope-missing-meta");
        var dlqTopic = _kafka.NewTopicName("envelope-missing-meta-dlq");
        var group = _kafka.NewGroupId("envelope-missing-meta-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            const string missingMetadataEnvelope = """
                {
                  "schemaVersion": 1,
                  "eventType": "fleet.telemetry.received",
                  "occurredAt": "2026-07-12T08:30:00Z",
                  "payload": {
                    "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    "vehicleId": "VH-ENV",
                    "driverId": "DRV-ENV",
                    "timestamp": "2026-07-12T08:30:00Z",
                    "latitude": 4.71,
                    "longitude": -74.07,
                    "speedKmh": 55.5,
                    "fuelLevelPercent": 60.0,
                    "batteryPercent": 75.0
                  }
                }
                """;

            await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "envelope-missing-meta-host",
                "envelope-missing-meta-host-group",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group,
                    ConnectionString = database.ConnectionString,
                    UseRealTimescaleProcessing = true,
                    UseProductionDeadLetterPublisher = true,
                    UseEventEnvelope = true
                });

            await host.StartAsync();
            Produce(topic, missingMetadataEnvelope, key: null, _kafka.BootstrapServers);
            await WaitUntilCommittedOffsetAsync(group, topic, 1, TimeSpan.FromSeconds(60));

            using var dlqConsumer = CreateDlqConsumer(group, dlqTopic, _kafka.BootstrapServers);
            var dlqMessage = ConsumeOne(dlqConsumer, TimeSpan.FromSeconds(30));
            Assert.NotNull(dlqMessage);

            var dlqJson = KafkaDlqTestHelper.ParseDlqPayload(dlqMessage!.Message.Value!);
            Assert.Equal("invalid_envelope", dlqJson.GetProperty("reason").GetString());

            using var scope = CreateDbScope(database.ConnectionString);
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            var contradictoryEventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            Assert.Equal(0, await db.TelemetryEvents.CountAsync(e => e.EventId == contradictoryEventId));
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(topic, dlqTopic);
        }
    }

    [Fact]
    public async Task SchemaVersion_futura_con_payload_incompatible_dlq_unsupported_schema_version()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        var topic = _kafka.NewTopicName("envelope-v2");
        var dlqTopic = _kafka.NewTopicName("envelope-v2-dlq");
        var group = _kafka.NewGroupId("envelope-v2-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            const string futureVersionEnvelope = """
                {
                  "schemaVersion": 2,
                  "eventType": "fleet.telemetry.received.v2",
                  "eventId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "occurredAt": "2026-07-12T08:30:00Z",
                  "payload": {
                    "telemetry": {
                      "vehicleRef": "VH-ENV",
                      "coords": [4.71, -74.07]
                    }
                  }
                }
                """;

            await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "envelope-v2-host",
                "envelope-v2-host-group",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group,
                    ConnectionString = database.ConnectionString,
                    UseRealTimescaleProcessing = true,
                    UseProductionDeadLetterPublisher = true,
                    UseEventEnvelope = true
                });

            await host.StartAsync();
            Produce(topic, futureVersionEnvelope, key: null, _kafka.BootstrapServers);
            await WaitUntilCommittedOffsetAsync(group, topic, 1, TimeSpan.FromSeconds(60));

            using var dlqConsumer = CreateDlqConsumer(group, dlqTopic, _kafka.BootstrapServers);
            var dlqMessage = ConsumeOne(dlqConsumer, TimeSpan.FromSeconds(30));
            Assert.NotNull(dlqMessage);

            var dlqJson = KafkaDlqTestHelper.ParseDlqPayload(dlqMessage!.Message.Value!);
            Assert.Equal("unsupported_schema_version", dlqJson.GetProperty("reason").GetString());

            using var scope = CreateDbScope(database.ConnectionString);
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            var futureVersionEventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            Assert.Equal(0, await db.TelemetryEvents.CountAsync(e => e.EventId == futureVersionEventId));
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(topic, dlqTopic);
        }
    }

    [Fact]
    public async Task Legacy_con_schemaVersion_agregado_dlq_invalid_envelope_cuando_use_event_envelope_false()
    {
        await using var database = new IntegrationTestDatabase();
        await database.InitializeAsync();

        var topic = _kafka.NewTopicName("legacy-reserved");
        var dlqTopic = _kafka.NewTopicName("legacy-reserved-dlq");
        var group = _kafka.NewGroupId("legacy-reserved-group");
        await _kafka.CreateTopicAsync(topic);
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            var original = CreateSampleEvent();
            var legacyWithReserved = TelemetryEventJsonSerializer.Serialize(original, useEnvelope: false);
            legacyWithReserved = legacyWithReserved.TrimEnd('}') + ",\"schemaVersion\":1}";

            await using var host = await TelemetryConsumerWorkerTestHost.CreateAsync(
                _kafka,
                "legacy-reserved-host",
                "legacy-reserved-host-group",
                new TelemetryConsumerWorkerHostOptions
                {
                    ExistingTopic = topic,
                    ExistingDeadLetterTopic = dlqTopic,
                    ExistingGroupId = group,
                    ConnectionString = database.ConnectionString,
                    UseRealTimescaleProcessing = true,
                    UseProductionDeadLetterPublisher = true,
                    UseEventEnvelope = false
                });

            await host.StartAsync();
            Produce(topic, legacyWithReserved, original.VehicleId, _kafka.BootstrapServers);
            await WaitUntilCommittedOffsetAsync(group, topic, 1, TimeSpan.FromSeconds(60));

            using var dlqConsumer = CreateDlqConsumer(group, dlqTopic, _kafka.BootstrapServers);
            var dlqMessage = ConsumeOne(dlqConsumer, TimeSpan.FromSeconds(30));
            Assert.NotNull(dlqMessage);

            var dlqJson = KafkaDlqTestHelper.ParseDlqPayload(dlqMessage!.Message.Value!);
            Assert.Equal("invalid_envelope", dlqJson.GetProperty("reason").GetString());

            using var scope = CreateDbScope(database.ConnectionString);
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            Assert.Equal(0, await db.TelemetryEvents.CountAsync(e => e.EventId == original.EventId));
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(topic, dlqTopic);
        }
    }

    private static TelemetryEvent CreateSampleEvent(Guid? eventId = null)
    {
        return TelemetryEvent.Create(
            eventId ?? Guid.NewGuid(),
            "VH-ENV",
            "DRV-ENV",
            new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero),
            4.71,
            -74.07,
            55.5,
            60.0,
            75.0,
            "gps");
    }

    private void Produce(string topic, string payload, string? key = null, string? bootstrap = null)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrap ?? _kafka.BootstrapServers,
            Acks = Acks.All
        }).Build();

        producer.Produce(topic, new Message<string, string> { Key = key ?? "k", Value = payload });
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    private static IServiceScope CreateDbScope(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FleetDbContext>(o => o.UseNpgsql(connectionString));
        return services.BuildServiceProvider().CreateScope();
    }

    private static IConsumer<string, string> CreateDlqConsumer(string group, string topic, string bootstrap)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = $"{group}-dlq-verify",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
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
                new ConsumerGroupTopicPartitions(groupId, [new TopicPartition(topic, 0)])
            ]);

            var partition = offsets.FirstOrDefault()?.Partitions.FirstOrDefault();
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
}
