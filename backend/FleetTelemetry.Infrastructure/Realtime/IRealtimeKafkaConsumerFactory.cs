using Confluent.Kafka;

namespace FleetTelemetry.Infrastructure.Realtime;

// Sesión de consumidor Kafka con Assign manual (sin Subscribe).
internal interface IRealtimeKafkaConsumerSession : IRealtimeKafkaPushTransport, IDisposable
{
    IReadOnlyList<TopicPartition> Assignment { get; }

    WatermarkOffsets QueryWatermarkOffsets(TopicPartition partition, TimeSpan timeout);

    void Assign(IEnumerable<TopicPartitionOffset> offsets);

    void Close();
}

// Crea sesiones nuevas; tras FatalFailure se dispone la anterior y se pide otra.
internal interface IRealtimeKafkaConsumerFactory
{
    IRealtimeKafkaConsumerSession Create(string groupId);
}

// Implementación Confluent.Kafka de producción.
internal sealed class ConfluentRealtimeKafkaConsumerFactory : IRealtimeKafkaConsumerFactory
{
    private readonly string _bootstrapServers;
    private readonly string _topic;

    public ConfluentRealtimeKafkaConsumerFactory(string bootstrapServers, string topic)
    {
        _bootstrapServers = bootstrapServers;
        _topic = topic;
    }

    public IRealtimeKafkaConsumerSession Create(string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Latest
        };

        var consumer = new ConsumerBuilder<string, string>(config).Build();
        return new ConfluentRealtimeKafkaConsumerSession(consumer, _topic);
    }
}

internal sealed class ConfluentRealtimeKafkaConsumerSession : IRealtimeKafkaConsumerSession
{
    private readonly IConsumer<string, string> _consumer;
    private readonly string _topic;
    private readonly Dictionary<TopicPartition, long> _assignedOffsets = new();
    private ConsumeResult<string, string>? _prefetched;
    private bool _disposed;

    public ConfluentRealtimeKafkaConsumerSession(IConsumer<string, string> consumer, string topic)
    {
        _consumer = consumer;
        _topic = topic;
    }

    public string Topic => _topic;

    public IReadOnlyList<TopicPartition> Assignment => _consumer.Assignment;

    public ConsumeResult<string, string>? Consume(TimeSpan timeout)
    {
        if (_prefetched is not null)
        {
            var pending = _prefetched;
            _prefetched = null;
            return pending;
        }

        return _consumer.Consume(timeout);
    }

    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition partition, TimeSpan timeout) =>
        _consumer.QueryWatermarkOffsets(partition, timeout);

    public void Assign(IEnumerable<TopicPartitionOffset> offsets)
    {
        var targets = offsets.ToList();
        _assignedOffsets.Clear();
        foreach (var tpo in targets)
            _assignedOffsets[tpo.TopicPartition] = tpo.Offset.Value;

        _consumer.Assign(targets);
        MaterializeAssignment(TimeSpan.FromSeconds(10));
    }

    // Assign es local; el cliente a veces no lo aplica hasta un poll. Descendemos offsets
    // inferiores al Assign (sin Seek) y prefabricamos el primer registro válido.
    private void MaterializeAssignment(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var poll = remaining < TimeSpan.FromMilliseconds(100)
                ? remaining
                : TimeSpan.FromMilliseconds(100);
            var result = _consumer.Consume(poll);
            if (result is null)
            {
                // Idle tras Assign en High (o tip): la posición quedó materializada.
                return;
            }

            if (IsAtOrAfterAssigned(result))
            {
                _prefetched = result;
                return;
            }

            // Offset anterior al Assign: descartar hasta materializar la posición.
        }
    }

    private bool IsAtOrAfterAssigned(ConsumeResult<string, string> result)
    {
        var partition = result.TopicPartition;
        if (!_assignedOffsets.TryGetValue(partition, out var assigned))
            return true;

        return result.Offset.Value >= assigned;
    }

    public void Close()
    {
        try
        {
            _consumer.Close();
        }
        catch (ObjectDisposedException)
        {
            // Ya cerrado.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _prefetched = null;
        try
        {
            Close();
        }
        finally
        {
            _consumer.Dispose();
        }
    }
}
