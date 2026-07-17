namespace FleetTelemetry.Infrastructure.Configuration;

public enum SseDeliveryMode
{
    Polling,
    KafkaPush
}

public class SseOptions
{
    public const string SectionName = "Sse";

    public SseDeliveryMode Mode { get; set; } = SseDeliveryMode.Polling;

    public string InstanceId { get; set; } = string.Empty;

    // Exige una sola partición en fleet.realtime para orden global de offsets.
    public bool RequireSingleRealtimePartition { get; set; } = true;

    // Eventos SSE retenidos para replay con Last-Event-ID.
    public int ReplayBufferSize { get; set; } = 200;

    public int SubscriberChannelCapacity { get; set; } = 100;

    public int ActivePollIntervalSeconds { get; set; } = 3;

    public int IdlePollIntervalSeconds { get; set; } = 10;

    // Frecuencia de heartbeat para mantener conexiones vivas.
    public int HeartbeatIntervalSeconds { get; set; } = 15;

    public int AlertBatchSize { get; set; } = 100;

    // Intervalo del servicio de expiración de conectividad (KafkaPush).
    public int ConnectivityExpiryIntervalSeconds { get; set; } = 30;

    // Ventana incremental consultada sobre LastTimestamp al cruzar el umbral.
    public int ConnectivityExpiryLookbackSeconds { get; set; } = 90;

    public int ConnectivityExpiryBatchSize { get; set; } = 200;
}
