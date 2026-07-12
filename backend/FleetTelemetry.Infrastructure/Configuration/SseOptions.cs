namespace FleetTelemetry.Infrastructure.Configuration;

public enum SseDeliveryMode
{
    Polling,
    KafkaPush
}

// Intervalos del poller SSE para el dashboard en tiempo real.
public class SseOptions
{
    public const string SectionName = "Sse";

    public SseDeliveryMode Mode { get; set; } = SseDeliveryMode.Polling;

    // Identidad estable de la réplica API (p. ej. HOSTNAME).
    public string InstanceId { get; set; } = Environment.MachineName;

    // Exige una sola partición en fleet.realtime para orden global de offsets.
    public bool RequireSingleRealtimePartition { get; set; } = true;

    // Eventos SSE retenidos para replay con Last-Event-ID.
    public int ReplayBufferSize { get; set; } = 200;

    // Capacidad por suscriptor antes de descartar (backpressure).
    public int SubscriberChannelCapacity { get; set; } = 100;

    // Intervalo cuando hay cambios recientes o primera consulta.
    public int ActivePollIntervalSeconds { get; set; } = 3;

    // Intervalo cuando la flota no cambió (reduce carga en DB).
    public int IdlePollIntervalSeconds { get; set; } = 10;

    // Frecuencia de heartbeat para mantener conexiones vivas.
    public int HeartbeatIntervalSeconds { get; set; } = 15;

    // Máximo de alertas por ciclo de polling.
    public int AlertBatchSize { get; set; } = 100;

    // Intervalo del servicio de expiración de conectividad (KafkaPush).
    public int ConnectivityExpiryIntervalSeconds { get; set; } = 30;

    // Ventana incremental consultada sobre LastTimestamp al cruzar el umbral.
    public int ConnectivityExpiryLookbackSeconds { get; set; } = 90;

    // Tope de vehículos evaluados por ciclo de expiración.
    public int ConnectivityExpiryBatchSize { get; set; } = 200;
}
