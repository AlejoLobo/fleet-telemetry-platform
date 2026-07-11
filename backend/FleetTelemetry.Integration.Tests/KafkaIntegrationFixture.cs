using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;

namespace FleetTelemetry.Integration.Tests;

// Broker Kafka compatible (confluent-local vía Testcontainers, Redpanda local o variable de entorno).
public sealed class KafkaIntegrationFixture : IAsyncLifetime
{
    public const string BootstrapEnvVar = "FLEET_INTEGRATION_KAFKA_BOOTSTRAP";

    private static readonly string[] IntegrationTopicPrefixes =
    [
        "worker-",
        "dlq-publisher-"
    ];

    private static readonly HashSet<string> ProtectedTopics = new(StringComparer.Ordinal)
    {
        "telemetry.raw",
        "telemetry.dead-letter",
        "__consumer_offsets"
    };

    private readonly ConcurrentDictionary<string, byte> _trackedTopics = new(StringComparer.Ordinal);

    private KafkaContainer? _container;

    public string BootstrapServers { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(BootstrapEnvVar);
        if (!string.IsNullOrWhiteSpace(external))
        {
            BootstrapServers = external.Trim();
            await PurgeStaleIntegrationTopicsAsync();
            return;
        }

        if (await CanConnectAsync("localhost:19092", TimeSpan.FromSeconds(3)))
        {
            BootstrapServers = "localhost:19092";
            await PurgeStaleIntegrationTopicsAsync();
            return;
        }

        _container = new KafkaBuilder()
            .WithImage("confluentinc/confluent-local:7.6.1") // Kafka-compatible (no Redpanda)
            .Build();

        await _container.StartAsync();
        BootstrapServers = _container.GetBootstrapAddress();
    }

    public async Task DisposeAsync()
    {
        await DeleteTrackedTopicsAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public string NewTopicName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant();

    public string NewGroupId(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant();

    public async Task CreateTopicAsync(string topic, int partitions = 1, short replicationFactor = 1)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = BootstrapServers,
            SocketTimeoutMs = 5_000
        }).Build();

        try
        {
            await admin.CreateTopicsAsync(
            [
                new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = partitions,
                    ReplicationFactor = replicationFactor
                }
            ]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }

        _trackedTopics.TryAdd(topic, 0);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var meta = admin.GetMetadata(topic, TimeSpan.FromSeconds(2));
            if (meta.Topics.Any(t => t.Topic == topic && t.Error.Code == ErrorCode.NoError && t.Partitions.Count >= partitions))
                return;

            await Task.Delay(200);
        }

        throw new TimeoutException($"Topic {topic} no quedó listo a tiempo.");
    }

    public Task DeleteTrackedTopicsAsync(params string[] topics)
    {
        var toDelete = topics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var topic in toDelete)
            _trackedTopics.TryRemove(topic, out _);

        return toDelete.Length == 0
            ? Task.CompletedTask
            : DeleteTopicsAsync(toDelete);
    }

    public async Task DeleteTrackedTopicsAsync()
    {
        var topics = _trackedTopics.Keys.ToArray();
        foreach (var topic in topics)
            _trackedTopics.TryRemove(topic, out _);

        if (topics.Length > 0)
            await DeleteTopicsAsync(topics);
    }

    private async Task PurgeStaleIntegrationTopicsAsync()
    {
        if (string.IsNullOrWhiteSpace(BootstrapServers))
            return;

        using var admin = CreateAdminClient();
        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));
        var staleTopics = metadata.Topics
            .Where(topic => topic.Error.Code == ErrorCode.NoError)
            .Select(topic => topic.Topic)
            .Where(topic => !ProtectedTopics.Contains(topic))
            .Where(IsIntegrationTestTopic)
            .ToArray();

        if (staleTopics.Length > 0)
            await DeleteTopicsAsync(staleTopics);
    }

    private async Task DeleteTopicsAsync(IReadOnlyCollection<string> topics)
    {
        if (topics.Count == 0 || string.IsNullOrWhiteSpace(BootstrapServers))
            return;

        using var admin = CreateAdminClient();

        try
        {
            await admin.DeleteTopicsAsync(topics, new DeleteTopicsOptions
            {
                OperationTimeout = TimeSpan.FromSeconds(15),
                RequestTimeout = TimeSpan.FromSeconds(15)
            });
        }
        catch (DeleteTopicsException ex) when (ex.Results.All(r =>
            r.Error.Code is ErrorCode.UnknownTopicOrPart or ErrorCode.NoError))
        {
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(2));
            if (topics.All(topic => metadata.Topics.All(existing => existing.Topic != topic)))
                return;

            await Task.Delay(200);
        }
    }

    private IAdminClient CreateAdminClient() =>
        new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = BootstrapServers,
            SocketTimeoutMs = 5_000
        }).Build();

    private static bool IsIntegrationTestTopic(string topic) =>
        IntegrationTopicPrefixes.Any(prefix => topic.StartsWith(prefix, StringComparison.Ordinal));

    private static Task<bool> CanConnectAsync(string bootstrap, TimeSpan timeout)
    {
        return Task.Run(() =>
        {
            try
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig
                {
                    BootstrapServers = bootstrap,
                    SocketTimeoutMs = (int)timeout.TotalMilliseconds
                }).Build();
                var meta = admin.GetMetadata(timeout);
                return meta.Brokers.Count > 0;
            }
            catch
            {
                return false;
            }
        });
    }
}

[CollectionDefinition(Name)]
public sealed class KafkaIntegrationCollection : ICollectionFixture<KafkaIntegrationFixture>
{
    public const string Name = "KafkaIntegration";
}
