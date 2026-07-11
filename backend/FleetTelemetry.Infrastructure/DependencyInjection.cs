using FleetTelemetry.Application.Configuration;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Application.Validation;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Observability;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Realtime;
using FleetTelemetry.Infrastructure.Repositories;
using FleetTelemetry.Infrastructure.Resilience;
using FleetTelemetry.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.AddOptions<StoppedVehicleQueryOptions>()
            .Bind(configuration.GetSection(StoppedVehicleQueryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StoppedVehicleQueryOptions>, StoppedVehicleQueryOptionsValidator>();

        services.AddSingleton(sp =>
        {
            var sseBroker = profile == InfrastructureProfile.Api
                ? sp.GetService<FleetSseBroker>()
                : null;
            return new FleetTelemetryMetrics(sseBroker);
        });

        services.AddSingleton(TimeProvider.System);

        services.AddOptions<TelemetryIngestOptions>()
            .Bind(configuration.GetSection(TelemetryIngestOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TelemetryIngestOptions>, TelemetryIngestOptionsValidator>();
        services.AddSingleton<TelemetryEventValidator>();

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.AddSingleton<ICircuitBreakerStateRegistry, CircuitBreakerStateRegistry>();
        services.AddSingleton<ResiliencePipelineFactory>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<ITelemetryEventValidator, TelemetryEventValidatorService>();
        services.AddSingleton<ITelemetryDomainEventValidator, TelemetryDomainEventValidatorService>();

        if (profile == InfrastructureProfile.Api)
        {
            // Servicios expuestos por la API REST.
            RegisterTimescaleDb(services, configuration);

            services.AddSingleton<FleetSseBroker>(sp =>
            {
                var sseOptions = sp.GetRequiredService<IOptions<SseOptions>>().Value;
                return new FleetSseBroker(
                    sp.GetRequiredService<TimeProvider>(),
                    sseOptions.SubscriberChannelCapacity,
                    sseOptions.ReplayBufferSize);
            });

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
            services.AddSingleton<IFleetRealtimePublisher, KafkaFleetRealtimePublisher>();
        }

        return services;
    }

    // Activa el poller SSE de actualizaciones de flota.
    public static IServiceCollection AddFleetSsePolling(this IServiceCollection services)
    {
        services.AddHostedService<FleetSsePollerHostedService>();
        return services;
    }

    // Consume fleet.realtime y empuja al broker SSE (modo kafka-push).
    public static IServiceCollection AddFleetSseKafkaPush(this IServiceCollection services)
    {
        services.AddHostedService<FleetSseKafkaPushHostedService>();
        return services;
    }

    public static IServiceCollection AddFleetSseDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration.GetSection(SseOptions.SectionName).Get<SseOptions>()?.Mode
            ?? SseDeliveryMode.Polling;

        if (mode == SseDeliveryMode.KafkaPush)
            services.AddFleetSseKafkaPush();
        else
            services.AddFleetSsePolling();

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
