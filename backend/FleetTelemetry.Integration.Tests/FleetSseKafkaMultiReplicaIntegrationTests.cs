using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

// FT-005: fan-out Kafka entre réplicas lógicas api-1 y api-2.
[Collection(KafkaIntegrationCollection.Name)]
public class FleetSseKafkaMultiReplicaIntegrationTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public FleetSseKafkaMultiReplicaIntegrationTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task Dos_instancias_logicas_reciben_todos_los_eventos_con_mismo_id_y_orden()
    {
        var topic = $"fleet.realtime.ft005.{Guid.NewGuid():N}";
        await CreateSinglePartitionTopicAsync(topic);
        await WaitForTopicReadyAsync(topic);

        var brokerA = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);
        var brokerB = new FleetSseBroker(TimeProvider.System, channelCapacity: 50, replayBufferSize: 100);

        var subA = brokerA.SubscribeFrom(0);
        var subB = brokerB.SubscribeFrom(0);

        await ProduceVehicleUpdatesAsync(_kafka.BootstrapServers, topic, 5);

        await ConsumeIntoBrokerAsync(_kafka.BootstrapServers, topic, "api-1", brokerA, expectedMessages: 5);
        await ConsumeIntoBrokerAsync(_kafka.BootstrapServers, topic, "api-2", brokerB, expectedMessages: 5);

        var idsA = DrainLiveIds(subA);
        var idsB = DrainLiveIds(subB);

        Assert.Equal(5, idsA.Count);
        Assert.Equal(5, idsB.Count);
        Assert.Equal(idsA, idsB);
        Assert.Equal(idsA.OrderBy(id => id).ToArray(), idsA);
    }

    [Fact]
    public async Task Reiniciar_replica_genera_replay_gap_explicito()
    {
        var restarted = new FleetSseBroker(TimeProvider.System, replayBufferSize: 100);
        var subscription = restarted.SubscribeFrom(3);

        Assert.Equal(Application.Realtime.SseReplayStatus.ReplayGap, subscription.ReplayStatus);
        Assert.Equal("instance-restarted", subscription.ResetReason);
    }

    [Fact]
    public void KafkaPush_rechaza_topic_con_mas_de_una_particion()
    {
        var topic = $"fleet.realtime.ft005.multi.{Guid.NewGuid():N}";
        CreateMultiPartitionTopic(topic);

        Assert.Throws<InvalidOperationException>(() =>
            RealtimeTopicValidator.EnsureSinglePartition(
                _kafka.BootstrapServers,
                topic,
                required: true));
    }

    private async Task CreateSinglePartitionTopicAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.BootstrapServers
        }).Build();

        await admin.CreateTopicsAsync(
        [
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        ]);
    }

    private void CreateMultiPartitionTopic(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.BootstrapServers
        }).Build();

        admin.CreateTopicsAsync(
        [
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 2,
                ReplicationFactor = 1
            }
        ]).GetAwaiter().GetResult();
    }

    private static async Task ProduceVehicleUpdatesAsync(string bootstrapServers, string topic, int count)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        for (var index = 0; index < count; index++)
        {
            var message = FleetRealtimeKafkaMessage.Serialize(new FleetRealtimeKafkaMessage
            {
                EventType = Application.Realtime.FleetRealtimeEventTypes.VehicleUpdate,
                Payload = System.Text.Json.JsonDocument.Parse(
                    $$"""{"vehicleId":"VH-{{index:D3}}","status":"online"}""").RootElement,
                OccurredAt = DateTimeOffset.UtcNow,
                VehicleId = $"VH-{index:D3}"
            });

            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = $"VH-{index:D3}",
                Value = message
            });
        }

        producer.Flush(TimeSpan.FromSeconds(5));
    }

    private async Task WaitForTopicReadyAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.BootstrapServers
        }).Build();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(2));
            if (metadata.Topics.Exists(entry =>
                    entry.Topic == topic &&
                    entry.Error.Code == ErrorCode.NoError &&
                    entry.Partitions.Count == 1))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException($"Topic {topic} was not ready for integration test.");
    }

    private static async Task ConsumeIntoBrokerAsync(
        string bootstrapServers,
        string topic,
        string instanceId,
        FleetSseBroker broker,
        int expectedMessages)
    {
        _ = instanceId;
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"fleet-realtime-sse-{instanceId}-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();

        var partition = new TopicPartition(topic, 0);
        consumer.Assign(new TopicPartitionOffset(partition, Offset.Beginning));

        var consumed = 0;
        var idlePolls = 0;
        const int maxIdlePolls = 40;
        while (consumed < expectedMessages && idlePolls < maxIdlePolls)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message?.Value is null)
            {
                idlePolls += 1;
                continue;
            }

            idlePolls = 0;
            var payload = FleetRealtimeKafkaMessage.Deserialize(result.Message.Value);
            if (broker.TryPublishExternal(result.Offset.Value, payload.EventType, payload.Payload, payload.OccurredAt))
            {
                consumer.Commit(result);
                consumed += 1;
            }
        }

        Assert.Equal(expectedMessages, consumed);
        await Task.CompletedTask;
    }

    private static List<long> DrainLiveIds(Application.DTOs.SseSubscription subscription)
    {
        var ids = new List<long>();
        while (subscription.LiveReader.TryRead(out var evt))
            ids.Add(evt.StreamId);

        return ids;
    }
}
