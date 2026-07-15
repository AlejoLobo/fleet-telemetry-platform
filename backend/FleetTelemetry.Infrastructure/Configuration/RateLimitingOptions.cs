namespace FleetTelemetry.Infrastructure.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Por defecto desactivado (monitor + muchos dispositivos cada ~3 s).
    /// Si se activa, POST /api/telemetry y /api/telemetry/batch quedan exentos.
    /// </summary>
    public bool Enabled { get; set; } = false;
    /// <summary>Cuota por IP y ventana para rutas no exentas (si Enabled=true).</summary>
    public int PermitLimit { get; set; } = 600;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}
