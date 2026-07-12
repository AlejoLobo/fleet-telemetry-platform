using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Realtime;
using FleetTelemetry.Infrastructure.Repositories;
using FleetTelemetry.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Integration.Tests;

// Configuración DI compartida para pruebas FT-004 contra TimescaleDB real.
internal static class IntegrationTestServiceBootstrap
{
    internal static void AddFleetTelemetryIntegrationServices(
        IServiceCollection services,
        string connectionString,
        FakeTimeProvider timeProvider,
        Action<QueryLimitsOptions>? configureQueryLimits = null,
        FakeFleetRealtimePublisher? configurePublisher = null)
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<FleetDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddOptions<QueryLimitsOptions>();
        services.Configure<QueryLimitsOptions>(options =>
        {
            options.FleetDefaultPageSize = 100;
            options.FleetMaxPageSize = 500;
            options.HistoryDefaultPageSize = 200;
            options.HistoryMaxPageSize = 1000;
            options.HistoryMaxRangeDays = 7;
            options.OnlineThresholdMinutes = 5;
            configureQueryLimits?.Invoke(options);
        });
        services.AddSingleton<IValidateOptions<QueryLimitsOptions>, QueryLimitsOptionsValidator>();
        services.Configure<KafkaOptions>(options =>
        {
            options.TelemetryTopic = "telemetry.raw";
            options.DeadLetterTopic = "telemetry.dead-letter";
        });
        services.Configure<SseOptions>(options =>
        {
            options.Mode = SseDeliveryMode.Polling;
            options.ConnectivityExpiryIntervalSeconds = 30;
            options.ConnectivityExpiryLookbackSeconds = 90;
            options.ConnectivityExpiryBatchSize = 200;
        });

        services.AddSingleton<FleetConnectivityPublishTracker>();
        services.AddSingleton<FleetConnectivityExpiryState>();
        services.AddScoped<IFleetConnectivityExpiryService, FleetConnectivityExpiryService>();
        services.AddScoped<ITelemetryProcessingUnitOfWork, TimescaleTelemetryProcessingUnitOfWork>();
        if (configurePublisher is not null)
            services.AddSingleton<IFleetRealtimePublisher>(configurePublisher);
        else
            services.AddSingleton<IFleetRealtimePublisher, NoOpFleetRealtimePublisher>();
        services.AddScoped<IFleetQueryService, TimescaleFleetQueryService>();
        services.AddScoped<ITelemetryRepository, TimescaleTelemetryRepository>();
        services.AddScoped<IFleetStateAggregateRepository, TimescaleFleetStateAggregateRepository>();
        services.AddScoped<IOpsQueryService, OpsQueryService>();
    }

    internal static async Task ResetFleetDataAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE fleet_alerts, processed_events, fleet_vehicle_state, telemetry_events
            RESTART IDENTITY CASCADE;
            """);
    }
}

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
