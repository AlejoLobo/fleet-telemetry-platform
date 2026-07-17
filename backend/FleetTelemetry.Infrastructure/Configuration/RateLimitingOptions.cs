namespace FleetTelemetry.Infrastructure.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    /// <summary>Cuota global por IP para consultas REST (excluye health, SSE e ingesta).</summary>
    public int PermitLimit { get; set; } = 600;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
    /// <summary>Cuota de ingesta por identidad de dispositivo (sub / X-Device-Id / IP).</summary>
    public int TelemetryPermitLimit { get; set; } = 120;
    public int TelemetryWindowSeconds { get; set; } = 60;
}
