namespace FleetTelemetry.Infrastructure.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    /// <summary>Solicitudes permitidas por IP y ventana (el monitor pagina flota + telemetría).</summary>
    public int PermitLimit { get; set; } = 600;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}
