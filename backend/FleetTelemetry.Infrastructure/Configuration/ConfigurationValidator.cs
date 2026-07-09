using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Infrastructure.Configuration;

// Valida configuración sensible al arrancar en entornos no locales.
public static class ConfigurationValidator
{
    private const string DefaultJwtSecret = "fleet-telemetry-dev-secret-change-in-production-min-32-chars";
    private const string DefaultDbPassword = "Password=fleet";

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return;

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (auth.Enabled && auth.JwtSecret == DefaultJwtSecret)
        {
            throw new InvalidOperationException(
                "Auth:JwtSecret debe configurarse por variable de entorno en entornos no locales.");
        }

        var timescale = configuration.GetSection(TimescaleDbOptions.SectionName).Get<TimescaleDbOptions>();
        if (timescale?.ConnectionString?.Contains(DefaultDbPassword, StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException(
                "TimescaleDb:ConnectionString no debe usar credenciales por defecto en producción.");
        }

        var openAi = configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>();
        if (openAi?.Enabled == true && string.IsNullOrWhiteSpace(openAi.ApiKey))
        {
            throw new InvalidOperationException(
                "OpenAI:ApiKey es obligatoria cuando OpenAI está habilitado.");
        }
    }
}
