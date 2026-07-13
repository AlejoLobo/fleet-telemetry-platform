namespace FleetTelemetry.Application.Realtime;

// Resultado tipado al publicar un offset Kafka externo en el broker SSE.
public enum ExternalPublishResult
{
    Accepted,
    Duplicate,
    OutOfOrder
}
