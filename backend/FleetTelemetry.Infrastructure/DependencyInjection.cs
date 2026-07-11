using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Realtime;
using FleetTelemetry.Infrastructure.Repositories;
using FleetTelemetry.Infrastructure.Resilience;
using FleetTelemetry.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Registro de dependencias de infraestructura.
namespace FleetTelemetry.Infrastructure;

// Perfil de despliegue: API o Worker.
public enum InfrastructureProfile
{
    Api,
    Worker
}

// Configura servicios según perfil Api o Worker.
public static class DependencyInjection
{
    // Registra opciones, resiliencia y repositorios según perfil.
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        InfrastructureProfile profile)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<TimescaleDbOptions>(configuration.GetSection(TimescaleDbOptions.SectionName));
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));
        services.Configure<SseOptions>(configuration.GetSection(SseOptions.SectionName));
        services.Configure<StoppedVehicleQueryOptions>(configuration.GetSection(StoppedVehicleQueryOptions.SectionName));

        services.AddSingleton(TimeProvider.System);

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.AddSingleton<ICircuitBreakerStateRegistry, CircuitBreakerStateRegistry>();
        services.AddSingleton<ResiliencePipelineFactory>();
        services.AddSingleton<JwtTokenService>();

        if (profile == InfrastructureProfile.Api)
        {
            // Servicios expuestos por la API REST.
            RegisterTimescaleDb(services, configuration);

            services.AddSingleton<ITelemetryEventPublisher, KafkaTelemetryEventPublisher>();
            services.AddSingleton<FleetSseBroker>();

            services.AddScoped<ITelemetryRepository, TimescaleTelemetryRepository>();
            services.AddScoped<IFleetQueryService, TimescaleFleetQueryService>();
            services.AddScoped<IFleetOperationalQueryService, TimescaleFleetOperationalQueryService>();
            services.AddScoped<IAlertRepository, TimescaleAlertRepository>();
            services.AddScoped<IAnalyticsQueryService, TimescaleAnalyticsQueryService>();
            services.AddScoped<IOpsQueryService, OpsQueryService>();
            services.AddScoped<IReadinessCheckService, ReadinessCheckService>();
            services.AddScoped<AiOperationalTools>();
            services.AddScoped<AiToolRouter>();
            services.AddScoped<OperationalAiAgentService>();
            services.AddHttpClient<OpenAiPolishService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20);
            });
            services.AddScoped<IAiAgentService, HybridAiAgentService>();

            services.AddScoped<IngestTelemetryEventUseCase>();
            services.AddScoped<IngestTelemetryBatchUseCase>();
            services.AddScoped<AcknowledgeAlertUseCase>();
        }
        else
        {
            // Servicios del worker consumidor de Kafka.
            RegisterTimescaleDb(services, configuration);

            services.AddScoped<IIdempotencyStore, TimescaleIdempotencyStore>();
            services.AddScoped<ITelemetryProcessingUnitOfWork, TimescaleTelemetryProcessingUnitOfWork>();
            services.AddScoped<ITelemetryRepository, TimescaleTelemetryRepository>();
            services.AddScoped<IAlertRepository, TimescaleAlertRepository>();
            services.AddScoped<ProcessTelemetryEventUseCase>();
            services.AddSingleton<IDeadLetterPublisher, KafkaDeadLetterPublisher>();
        }

        return services;
    }

    // Activa el poller SSE de actualizaciones de flota.
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
