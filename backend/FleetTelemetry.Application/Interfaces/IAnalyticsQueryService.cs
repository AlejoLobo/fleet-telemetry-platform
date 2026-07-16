namespace FleetTelemetry.Application.Interfaces;

public interface IAnalyticsQueryService
{
    Task<double> GetAverageSpeedAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
