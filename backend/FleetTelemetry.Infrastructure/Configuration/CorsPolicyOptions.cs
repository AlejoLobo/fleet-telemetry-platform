namespace FleetTelemetry.Infrastructure.Configuration;

// Orígenes permitidos para CORS (configuración explícita, sin wildcard en producción).
public class CorsPolicyOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = ["http://localhost:3000"];
    public bool AllowAnyHeader { get; set; } = true;
    public bool AllowAnyMethod { get; set; } = true;
}
