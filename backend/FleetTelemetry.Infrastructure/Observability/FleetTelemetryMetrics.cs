using System.Diagnostics.Metrics;
using FleetTelemetry.Infrastructure.Realtime;

// Métricas operativas básicas del pipeline de telemetría.
namespace FleetTelemetry.Infrastructure.Observability;

public sealed class FleetTelemetryMetrics
{
    public const string MeterName = "FleetTelemetry";

    private readonly Meter _meter;

    public Counter<long> KafkaMessagesConsumed { get; }

    public Counter<long> DlqMessagesPublished { get; }

    public FleetTelemetryMetrics(FleetSseBroker? sseBroker = null)
    {
        _meter = new Meter(MeterName);

        KafkaMessagesConsumed = _meter.CreateCounter<long>(
            "fleet.kafka.messages.consumed",
            description: "Mensajes consumidos desde Kafka");

        DlqMessagesPublished = _meter.CreateCounter<long>(
            "fleet.kafka.dlq.published",
            description: "Mensajes publicados en el tópico dead-letter");

        if (sseBroker is not null)
        {
            _meter.CreateObservableGauge(
                "fleet.sse.subscribers",
                () => new Measurement<int>(sseBroker.SubscriberCount),
                description: "Suscriptores SSE activos");
        }
    }
}
