// Opciones de conexión a Kafka.
namespace FleetTelemetry.Infrastructure.Configuration;

// Bootstrap, tópico, grupo consumidor y reintentos de procesamiento.
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string TelemetryTopic { get; set; } = "telemetry.raw";
    public string DeadLetterTopic { get; set; } = "telemetry.dead-letter";
    public string ConsumerGroup { get; set; } = "telemetry-processor";

    // Intentos de procesamiento del mismo mensaje antes de DLQ.
    public int MaxProcessingAttempts { get; set; } = 3;

    // Backoff entre reintentos del mismo offset (exponencial + jitter).
    public int RetryInitialDelayMilliseconds { get; set; } = 500;
    public int RetryMaxDelayMilliseconds { get; set; } = 5000;
}
