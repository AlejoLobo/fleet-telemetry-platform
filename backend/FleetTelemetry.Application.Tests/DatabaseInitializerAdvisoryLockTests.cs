namespace FleetTelemetry.Application.Tests;

public class DatabaseInitializerAdvisoryLockTests
{
    [Fact]
    public void Worker_usa_advisory_lock_en_todos_los_entornos()
    {
        var workerSource = File.ReadAllText(
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "FleetTelemetry.Worker",
                "TelemetryConsumerWorker.cs")));

        Assert.DoesNotContain("useAdvisoryLock: environment.IsDevelopment()", workerSource);
        Assert.Contains("useAdvisoryLock: true", workerSource);
    }
}
