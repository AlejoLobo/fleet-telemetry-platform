namespace FleetTelemetry.Infrastructure.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Por defecto desactivado: el monitor pagina flota/telemetría cada 3–15 s.
    /// Activarlo solo en despliegues con cuota explícita.
    /// </summary>
    public bool Enabled { get; set; } = false;
    /// <summary>Solicitudes permitidas por IP y ventana (si Enabled=true).</summary>
    public int PermitLimit { get; set; } = 600;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}
