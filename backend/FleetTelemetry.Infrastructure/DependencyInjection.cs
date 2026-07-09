using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Realtime;
using FleetTelemetry.Infrastructure.Repositories;
using FleetTelemetry.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Infrastructure;

public enum InfrastructureProfile
{
    Api,
    Worker
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        InfrastructureProfile profile)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<TimescaleDbOptions>(configuration.GetSection(TimescaleDbOptions.SectionName));

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.AddSingleton<JwtTokenService>();

        if (profile == InfrastructureProfile.Api)
        {
            RegisterTimescaleDb(services, configuration);

            services.AddSingleton<ITelemetryEventPublisher, KafkaTelemetryEventPublisher>();
            services.AddSingleton<FleetSseBroker>();

            services.AddScoped<ITelemetryRepository, TimescaleTelemetryRepository>();
            services.AddScoped<IFleetQueryService, TimescaleFleetQueryService>();
            services.AddScoped<IAlertRepository, TimescaleAlertRepository>();
            services.AddScoped<IAnalyticsQueryService, TimescaleAnalyticsQueryService>();
            services.AddScoped<AiOperationalTools>();
            services.AddScoped<OperationalAiAgentService>();
            services.AddHttpClient<OpenAiPolishService>();
            services.AddScoped<IAiAgentService, HybridAiAgentService>();

            services.AddScoped<IngestTelemetryEventUseCase>();
            services.AddScoped<IngestTelemetryBatchUseCase>();
            services.AddScoped<AcknowledgeAlertUseCase>();
        }
        else
        {
            RegisterTimescaleDb(services, configuration);

            services.AddScoped<IIdempotencyStore, TimescaleIdempotencyStore>();
            services.AddScoped<ITelemetryRepository, TimescaleTelemetryRepository>();
            services.AddScoped<IAlertRepository, TimescaleAlertRepository>();
            services.AddScoped<ProcessTelemetryEventUseCase>();
        }

        return services;
    }

    public static IServiceCollection AddFleetSsePolling(this IServiceCollection services)
    {
        services.AddHostedService<FleetSsePollerHostedService>();
        return services;
    }

    private static void RegisterTimescaleDb(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetSection(TimescaleDbOptions.SectionName)
            .Get<TimescaleDbOptions>()?.ConnectionString
            ?? throw new InvalidOperationException("TimescaleDb connection string is required.");

        services.AddDbContext<FleetDbContext>(options =>
            options.UseNpgsql(connectionString));
    }
}
