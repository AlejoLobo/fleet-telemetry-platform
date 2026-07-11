using System.Diagnostics;

// Fuentes de actividad para trazas personalizadas del pipeline.
namespace FleetTelemetry.Infrastructure.Observability;

public static class FleetTelemetryActivitySources
{
    public const string WorkerSourceName = "FleetTelemetry.Worker";

    public static readonly ActivitySource Worker = new(WorkerSourceName);
}
