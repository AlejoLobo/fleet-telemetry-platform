namespace FleetTelemetry.Infrastructure.Configuration;

// Intervalos del poller SSE para el dashboard en tiempo real.
public class SseOptions
{
    public const string SectionName = "Sse";

    // Intervalo cuando hay cambios recientes o primera consulta.
    public int ActivePollIntervalSeconds { get; set; } = 3;

    // Intervalo cuando la flota no cambió (reduce carga en DB).
    public int IdlePollIntervalSeconds { get; set; } = 10;

    // Frecuencia de heartbeat para mantener conexiones vivas.
    public int HeartbeatIntervalSeconds { get; set; } = 15;

    // Máximo de alertas por ciclo de polling.
    public int AlertBatchSize { get; set; } = 100;
}
