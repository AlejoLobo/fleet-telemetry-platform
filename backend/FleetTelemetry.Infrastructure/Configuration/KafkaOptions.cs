namespace FleetTelemetry.Infrastructure.Configuration;

public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string TelemetryTopic { get; set; } = "telemetry.raw";
    public string ConsumerGroup { get; set; } = "telemetry-processor";
}
