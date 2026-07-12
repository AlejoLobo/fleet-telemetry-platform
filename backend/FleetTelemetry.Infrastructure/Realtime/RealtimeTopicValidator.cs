using Confluent.Kafka;

namespace FleetTelemetry.Infrastructure.Realtime;

// Valida que fleet.realtime tenga una sola partición para orden global de offsets SSE.
public static class RealtimeTopicValidator
{
    public static void EnsureSinglePartition(
        string bootstrapServers,
        string topic,
        bool required = true)
    {
        if (!required)
            return;

        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));
        var topicMetadata = metadata.Topics.FirstOrDefault(t =>
            string.Equals(t.Topic, topic, StringComparison.Ordinal));

        if (topicMetadata is null || topicMetadata.Error.IsError)
        {
            throw new InvalidOperationException(
                $"No se pudo obtener metadata del tópico {topic}.");
        }

        if (topicMetadata.Partitions.Count != 1)
        {
            throw new InvalidOperationException(
                $"El tópico {topic} debe tener exactamente 1 partición para replay SSE global. Particiones actuales: {topicMetadata.Partitions.Count}.");
        }
    }
}
