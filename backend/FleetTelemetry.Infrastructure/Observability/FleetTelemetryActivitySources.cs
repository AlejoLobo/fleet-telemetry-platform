using System.Diagnostics;

namespace FleetTelemetry.Infrastructure.Observability;

public static class FleetTelemetryActivitySources
{
    public const string WorkerSourceName = "FleetTelemetry.Worker";

    public static readonly ActivitySource Worker = new(WorkerSourceName);
}
