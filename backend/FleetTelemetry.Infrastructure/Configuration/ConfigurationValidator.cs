using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Infrastructure.Configuration;

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

        ValidateKafkaOptions(configuration);

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

    private static void ValidateKafkaOptions(IConfiguration configuration)
    {
        var kafka = configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();

        if (kafka.MaxProcessingAttempts < 1)
        {
            throw new InvalidOperationException(
                "Kafka:MaxProcessingAttempts debe ser mayor o igual a 1.");
        }

        if (kafka.RetryInitialDelayMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "Kafka:RetryInitialDelayMilliseconds debe ser mayor que 0.");
        }

        if (kafka.RetryMaxDelayMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "Kafka:RetryMaxDelayMilliseconds debe ser mayor que 0.");
        }

        if (kafka.RetryMaxDelayMilliseconds < kafka.RetryInitialDelayMilliseconds)
        {
            throw new InvalidOperationException(
                "Kafka:RetryMaxDelayMilliseconds debe ser mayor o igual a Kafka:RetryInitialDelayMilliseconds.");
        }

        if (kafka.MaxDeadLetterPublishAttempts < 1)
        {
            throw new InvalidOperationException(
                "Kafka:MaxDeadLetterPublishAttempts debe ser mayor o igual a 1.");
        }

        if (kafka.MaxPollIntervalMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "Kafka:MaxPollIntervalMilliseconds debe ser mayor que 0.");
        }
    }
}
