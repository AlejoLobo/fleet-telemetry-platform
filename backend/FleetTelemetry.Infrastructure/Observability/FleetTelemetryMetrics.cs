using System.Diagnostics;
using System.Diagnostics.Metrics;
using FleetTelemetry.Infrastructure.Realtime;

// Métricas operativas básicas del pipeline de telemetría.
namespace FleetTelemetry.Infrastructure.Observability;

public sealed class FleetTelemetryMetrics
{
    public const string MeterName = "FleetTelemetry";

    private readonly Meter _meter;

    public Counter<long> TelemetryIngestedTotal { get; }
    public Counter<long> TelemetryProcessedTotal { get; }
    public Counter<long> TelemetryDuplicateTotal { get; }
    public Counter<long> TelemetryInvalidTotal { get; }
    public Counter<long> TelemetryDlqTotal { get; }
    public Counter<long> KafkaPublishFailuresTotal { get; }
    public Counter<long> AiToolCallsTotal { get; }
    public Histogram<double> TelemetryProcessingDuration { get; }
    public Histogram<double> AiToolDuration { get; }
    public Counter<long> KafkaMessagesConsumed { get; }
    public Counter<long> DlqMessagesPublished { get; }

    public FleetTelemetryMetrics(FleetSseBroker? sseBroker = null)
    {
        _meter = new Meter(MeterName);

        TelemetryIngestedTotal = _meter.CreateCounter<long>(
            "telemetry_ingested_total",
            description: "Eventos de telemetría aceptados por la API");

        TelemetryProcessedTotal = _meter.CreateCounter<long>(
            "telemetry_processed_total",
            description: "Eventos de telemetría persistidos por el worker");

        TelemetryDuplicateTotal = _meter.CreateCounter<long>(
            "telemetry_duplicate_total",
            description: "Eventos duplicados omitidos por idempotencia");

        TelemetryInvalidTotal = _meter.CreateCounter<long>(
            "telemetry_invalid_total",
            description: "Payloads inválidos enviados a DLQ");

        TelemetryDlqTotal = _meter.CreateCounter<long>(
            "telemetry_dlq_total",
            description: "Mensajes publicados en dead-letter queue");

        KafkaPublishFailuresTotal = _meter.CreateCounter<long>(
            "kafka_publish_failures_total",
            description: "Fallos al publicar en Kafka desde la API");

        AiToolCallsTotal = _meter.CreateCounter<long>(
            "ai_tool_calls_total",
            description: "Invocaciones de herramientas del agente IA");

        TelemetryProcessingDuration = _meter.CreateHistogram<double>(
            "telemetry_processing_duration",
            unit: "s",
            description: "Duración del procesamiento de telemetría en el worker");

        AiToolDuration = _meter.CreateHistogram<double>(
            "ai_tool_duration",
            unit: "s",
            description: "Duración de ejecución de herramientas IA");

        KafkaMessagesConsumed = _meter.CreateCounter<long>(
            "fleet.kafka.messages.consumed",
            description: "Mensajes consumidos desde Kafka");

        DlqMessagesPublished = _meter.CreateCounter<long>(
            "fleet.kafka.dlq.published",
            description: "Mensajes publicados en el tópico dead-letter");

        if (sseBroker is not null)
        {
            _meter.CreateObservableGauge(
                "sse_subscribers",
                () => new Measurement<int>(sseBroker.SubscriberCount),
                description: "Suscriptores SSE activos");

            _meter.CreateObservableCounter(
                "fleet.sse.events_dropped_total",
                () => new Measurement<long>(sseBroker.DroppedEvents),
                description: "Eventos SSE descartados por suscriptores lentos");
        }
    }

    public void RecordAiToolCall(string toolName, TimeSpan duration, bool success)
    {
        AiToolCallsTotal.Add(1, new TagList { { "tool", toolName }, { "success", success } });
        AiToolDuration.Record(duration.TotalSeconds, new TagList { { "tool", toolName } });
    }

    public ProcessingDurationScope TrackProcessingDuration() => new(this);

    public sealed class ProcessingDurationScope : IDisposable
    {
        private readonly FleetTelemetryMetrics _metrics;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal ProcessingDurationScope(FleetTelemetryMetrics metrics) => _metrics = metrics;

        public void Dispose() => _metrics.TelemetryProcessingDuration.Record(_stopwatch.Elapsed.TotalSeconds);
    }
}
