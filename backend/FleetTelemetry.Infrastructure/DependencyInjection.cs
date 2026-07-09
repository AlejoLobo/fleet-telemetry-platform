using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Repositories;
using FleetTelemetry.Infrastructure.Mocks;
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

        if (profile == InfrastructureProfile.Api)
        {
            services.AddSingleton<ITelemetryEventPublisher, KafkaTelemetryEventPublisher>();
            services.AddSingleton<ITelemetryRepository, MockTelemetryRepository>();
            services.AddSingleton<IFleetQueryService, MockFleetQueryService>();
            services.AddSingleton<IAlertRepository, MockAlertRepository>();
            services.AddSingleton<IAnalyticsQueryService, MockAnalyticsQueryService>();
            services.AddSingleton<IAiAgentService, MockAiAgentService>();

            services.AddScoped<IngestTelemetryEventUseCase>();
            services.AddScoped<IngestTelemetryBatchUseCase>();
        }
        else
        {
            var connectionString = configuration.GetSection(TimescaleDbOptions.SectionName)
                .Get<TimescaleDbOptions>()?.ConnectionString
                ?? throw new InvalidOperationException("TimescaleDb connection string is required for Worker profile.");

            services.AddDbContext<FleetDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddScoped<IIdempotencyStore, TimescaleIdempotencyStore>();
            services.AddScoped<ITelemetryRepository, TimescaleTelemetryRepository>();
            services.AddScoped<IAlertRepository, TimescaleAlertRepository>();
            services.AddScoped<ProcessTelemetryEventUseCase>();
        }

        return services;
    }
}
