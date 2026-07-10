using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;

namespace FleetTelemetry.Integration.Tests;

// Broker Kafka real: variable de entorno, Redpanda local, o Testcontainers.
public sealed class KafkaIntegrationFixture : IAsyncLifetime
{
    public const string BootstrapEnvVar = "FLEET_INTEGRATION_KAFKA_BOOTSTRAP";

    private KafkaContainer? _container;

    public string BootstrapServers { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(BootstrapEnvVar);
        if (!string.IsNullOrWhiteSpace(external))
        {
            BootstrapServers = external.Trim();
            return;
        }

        if (await CanConnectAsync("localhost:19092", TimeSpan.FromSeconds(3)))
        {
            BootstrapServers = "localhost:19092";
            return;
        }

        _container = new KafkaBuilder()
            .WithImage("confluentinc/confluent-local:7.6.1")
            .Build();

        await _container.StartAsync();
        BootstrapServers = _container.GetBootstrapAddress();
    }

    public async Task DisposeAsync()
    {
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
            BootstrapServers = BootstrapServers
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
