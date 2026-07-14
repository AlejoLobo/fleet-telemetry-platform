// Opciones de conexión a Kafka.
namespace FleetTelemetry.Infrastructure.Configuration;

// Bootstrap, tópico, grupo consumidor y reintentos de procesamiento.
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string TelemetryTopic { get; set; } = "telemetry.raw";
    public string DeadLetterTopic { get; set; } = "telemetry.dead-letter";
    public string RealtimeTopic { get; set; } = "fleet.realtime";
    public string RealtimeConsumerGroupBase { get; set; } = "fleet-realtime-sse";

    // Compatibilidad con configuración anterior.
    public string RealtimeConsumerGroup
    {
        get => RealtimeConsumerGroupBase;
        set => RealtimeConsumerGroupBase = value;
    }
    public string ConsumerGroup { get; set; } = "telemetry-processor";

    // Intentos de procesamiento del mismo mensaje antes de DLQ.
    public int MaxProcessingAttempts { get; set; } = 3;

    // Backoff entre reintentos del mismo offset (exponencial + jitter).
    public int RetryInitialDelayMilliseconds { get; set; } = 500;
    public int RetryMaxDelayMilliseconds { get; set; } = 5000;

    // Intentos de publicación DLQ antes de detener el Worker sin commit.
    public int MaxDeadLetterPublishAttempts { get; set; } = 5;

    // Debe superar el peor escenario de procesamiento + Polly + backoff + DLQ.
    public int MaxPollIntervalMilliseconds { get; set; } = 300_000;

    // Timeout de produce del publicador DLQ/ingesta (ms).
    public int ProducerMessageTimeoutMs { get; set; } = 10_000;

    // Máximo de particiones procesadas en paralelo (orden estricto por partición).
    public int MaxConcurrentPartitions { get; set; } = 4;

    // Tamaño máximo de lote de publicación de ingesta.
    public int PublishBatchSize { get; set; } = 50;

    // Serializar con envelope versionado en Kafka (false mantiene compatibilidad con consumidores actuales).
    public bool UseEventEnvelope { get; set; } = false;
}
