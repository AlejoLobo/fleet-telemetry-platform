using FleetTelemetry.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Mocks;

public class MockAnalyticsQueryService : IAnalyticsQueryService
{
    private readonly ILogger<MockAnalyticsQueryService> _logger;

    public MockAnalyticsQueryService(ILogger<MockAnalyticsQueryService> logger)
    {
        _logger = logger;
    }

    public Task<double> GetAverageSpeedAsync(
        string vehicleId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MOCK] GetAverageSpeedAsync called for vehicle {VehicleId} (returning mock average 38.2 km/h)",
            vehicleId);

        return Task.FromResult(38.2);
    }
}
