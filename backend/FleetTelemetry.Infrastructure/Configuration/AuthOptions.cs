// Opciones de autenticación JWT.
namespace FleetTelemetry.Infrastructure.Configuration;

// Configuración de tokens y credenciales de demo.
public class AuthOptions
{
    public const string SectionName = "Auth";

    public bool Enabled { get; set; }
    public string JwtSecret { get; set; } = "fleet-telemetry-dev-secret-change-in-production-min-32-chars";
    public string JwtIssuer { get; set; } = "fleet-telemetry";
    public string JwtAudience { get; set; } = "fleet-clients";
    public int TokenExpirationMinutes { get; set; } = 480;
    public string DemoUsername { get; set; } = "admin";
    public string DemoPassword { get; set; } = "fleet2026";
}
