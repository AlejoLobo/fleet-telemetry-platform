// Opciones de exportación OTLP (trazas, métricas y logs).
namespace FleetTelemetry.Infrastructure.Configuration;

public class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public bool Enabled { get; set; }

    public string ServiceName { get; set; } = "FleetTelemetry";

    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    // Valores admitidos: grpc | http/protobuf
    public string OtlpProtocol { get; set; } = "grpc";
}
