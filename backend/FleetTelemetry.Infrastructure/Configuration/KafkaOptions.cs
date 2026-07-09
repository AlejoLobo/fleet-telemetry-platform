// Opciones de conexión a Kafka.
namespace FleetTelemetry.Infrastructure.Configuration;

// Bootstrap, tópico y grupo consumidor.
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string TelemetryTopic { get; set; } = "telemetry.raw";
    public string ConsumerGroup { get; set; } = "telemetry-processor";
}
