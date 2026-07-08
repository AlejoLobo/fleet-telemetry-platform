using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.UseCases;
using FleetTelemetry.Infrastructure.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace FleetTelemetry.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ITelemetryEventPublisher, MockTelemetryEventPublisher>();
        services.AddSingleton<ITelemetryRepository, MockTelemetryRepository>();
        services.AddSingleton<IFleetQueryService, MockFleetQueryService>();
        services.AddSingleton<IAlertRepository, MockAlertRepository>();
        services.AddSingleton<IAnalyticsQueryService, MockAnalyticsQueryService>();
        services.AddSingleton<IAiAgentService, MockAiAgentService>();

        services.AddScoped<IngestTelemetryEventUseCase>();
        services.AddScoped<IngestTelemetryBatchUseCase>();

        return services;
    }
}
