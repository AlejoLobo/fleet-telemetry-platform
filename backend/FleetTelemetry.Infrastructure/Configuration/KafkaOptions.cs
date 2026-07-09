// Opciones de conexión a Kafka.
namespace FleetTelemetry.Infrastructure.Configuration;

// Bootstrap, tópico y grupo consumidor.
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string TelemetryTopic { get; set; } = "telemetry.raw";
    public string DlqTopic { get; set; } = "telemetry.dlq";
    public string ConsumerGroup { get; set; } = "telemetry-processor";

    // Reintentos antes de enviar a DLQ por fallo persistente de procesamiento.
    public int MaxProcessingAttempts { get; set; } = 3;
}

