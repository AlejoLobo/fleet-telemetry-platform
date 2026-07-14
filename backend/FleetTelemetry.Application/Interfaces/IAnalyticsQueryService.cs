namespace FleetTelemetry.Application.Interfaces;

public interface IAnalyticsQueryService
{
    Task<double> GetAverageSpeedAsync(string vehicleId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
