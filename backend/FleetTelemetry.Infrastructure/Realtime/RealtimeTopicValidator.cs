namespace FleetTelemetry.Infrastructure.Realtime;

// Valida que fleet.realtime tenga una sola partición para orden global de offsets SSE.
public static class RealtimeTopicValidator
{
    public static void EnsureSinglePartition(
        IRealtimeTopicMetadataSource metadataSource,
        string topic,
        bool required = true,
        TimeSpan? timeout = null)
    {
        if (!required)
            return;

        var partitionCount = metadataSource.GetPartitionCount(
            topic,
            timeout ?? TimeSpan.FromSeconds(10));

        if (partitionCount != 1)
            throw new RealtimeTopicPartitionCountException(topic, partitionCount);
    }

    public static void EnsureSinglePartition(
        string bootstrapServers,
        string topic,
        bool required = true)
    {
        EnsureSinglePartition(
            new ConfluentRealtimeTopicMetadataSource(bootstrapServers),
            topic,
            required);
    }
}
