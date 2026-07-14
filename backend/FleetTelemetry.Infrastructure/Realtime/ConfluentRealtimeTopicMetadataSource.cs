using Confluent.Kafka;

namespace FleetTelemetry.Infrastructure.Realtime;

public sealed class ConfluentRealtimeTopicMetadataSource : IRealtimeTopicMetadataSource
{
    private readonly string _bootstrapServers;

    public ConfluentRealtimeTopicMetadataSource(string bootstrapServers)
    {
        _bootstrapServers = bootstrapServers;
    }

    public int GetPartitionCount(string topic, TimeSpan timeout)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _bootstrapServers
            }).Build();

            var metadata = admin.GetMetadata(topic, timeout);
            var topicMetadata = metadata.Topics.FirstOrDefault(t =>
                string.Equals(t.Topic, topic, StringComparison.Ordinal));

            if (topicMetadata is null)
            {
                throw new RealtimeTopicMetadataUnavailableException(
                    $"El tópico {topic} aún no está disponible en metadata.");
            }

            if (topicMetadata.Error.IsError)
            {
                throw new RealtimeTopicMetadataUnavailableException(
                    $"No se pudo obtener metadata del tópico {topic}: {topicMetadata.Error.Reason}");
            }

            return topicMetadata.Partitions.Count;
        }
        catch (RealtimeTopicMetadataUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RealtimeTopicMetadataUnavailableException(
                $"Metadata del tópico {topic} temporalmente inaccesible.",
                ex);
        }
    }
}
