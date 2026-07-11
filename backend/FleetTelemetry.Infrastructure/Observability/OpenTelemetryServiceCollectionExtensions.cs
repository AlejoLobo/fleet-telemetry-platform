using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Registro de trazas, métricas y correlación de logs vía OTLP.
namespace FleetTelemetry.Infrastructure.Observability;

public static class OpenTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddFleetOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        InfrastructureProfile profile)
    {
        var options = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>()
            ?? new OpenTelemetryOptions();

        if (!options.Enabled)
            return services;

        var serviceName = ResolveServiceName(options, profile);
        var otlpProtocol = ResolveOtlpProtocol(options.OtlpProtocol);

        var configureOtlp = CreateOtlpConfigurator(options.OtlpEndpoint, otlpProtocol);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddSource(FleetTelemetryActivitySources.WorkerSourceName)
                    .AddOtlpExporter(configureOtlp);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(FleetTelemetryMetrics.MeterName)
                    .AddOtlpExporter(configureOtlp);
            });

        return services;
    }

    public static ILoggingBuilder AddFleetOpenTelemetryLogging(
        this ILoggingBuilder logging,
        IConfiguration configuration,
        InfrastructureProfile profile)
    {
        var options = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>()
            ?? new OpenTelemetryOptions();

        if (!options.Enabled)
            return logging;

        var otlpProtocol = ResolveOtlpProtocol(options.OtlpProtocol);

        var configureOtlp = CreateOtlpConfigurator(options.OtlpEndpoint, otlpProtocol);

        logging.AddOpenTelemetry(openTelemetry =>
        {
            openTelemetry.IncludeFormattedMessage = true;
            openTelemetry.IncludeScopes = true;
            openTelemetry.ParseStateValues = true;
            openTelemetry.SetResourceBuilder(
                ResourceBuilder.CreateDefault().AddService(ResolveServiceName(options, profile)));

            openTelemetry.AddOtlpExporter(configureOtlp);
        });

        return logging;
    }

    private static Action<OtlpExporterOptions> CreateOtlpConfigurator(string? endpoint, OtlpExportProtocol protocol) =>
        otlp =>
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
                otlp.Endpoint = new Uri(endpoint);

            otlp.Protocol = protocol;
        };

    private static string ResolveServiceName(OpenTelemetryOptions options, InfrastructureProfile profile) =>
        string.IsNullOrWhiteSpace(options.ServiceName)
            ? profile == InfrastructureProfile.Api ? "FleetTelemetry.Api" : "FleetTelemetry.Worker"
            : options.ServiceName;

    private static OtlpExportProtocol ResolveOtlpProtocol(string? protocol) =>
        string.Equals(protocol, "http/protobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
}
