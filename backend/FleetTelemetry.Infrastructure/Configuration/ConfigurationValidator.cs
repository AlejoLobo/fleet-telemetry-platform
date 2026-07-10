using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Infrastructure.Configuration;

// Valida configuración sensible al arrancar.
public static class ConfigurationValidator
{
    private const string DefaultDbPassword = "Password=fleet";

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (auth.Enabled)
        {
            if (string.IsNullOrWhiteSpace(auth.JwtSecret) || auth.JwtSecret.Length < AuthOptions.MinimumJwtSecretLength)
            {
                throw new InvalidOperationException(
                    $"Auth:JwtSecret debe tener al menos {AuthOptions.MinimumJwtSecretLength} caracteres cuando Auth está habilitado.");
            }

            if (string.IsNullOrWhiteSpace(auth.DemoPassword))
            {
                throw new InvalidOperationException(
                    "Auth:DemoPassword no debe estar vacío cuando Auth está habilitado.");
            }
        }

        if (environment.IsDevelopment())
            return;

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
