namespace FleetTelemetry.Infrastructure.Configuration;

public class AuthOptions
{
    public const string SectionName = "Auth";
    public const int MinimumJwtSecretLength = 32;

    public bool Enabled { get; set; }
    public string JwtSecret { get; set; } = string.Empty;
    public string JwtIssuer { get; set; } = "fleet-telemetry";
    public string JwtAudience { get; set; } = "fleet-clients";
    public int TokenExpirationMinutes { get; set; } = 480;

    /// <summary>Operador demo sin device:manage (portal / lectura).</summary>
    public string DemoUsername { get; set; } = "admin";
    public string DemoPassword { get; set; } = string.Empty;

    /// <summary>
    /// Administrador demo con device:manage (renombrar cualquier dispositivo).
    /// No recibe telemetry:write; no puede publicar como dispositivo.
    /// </summary>
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
}
