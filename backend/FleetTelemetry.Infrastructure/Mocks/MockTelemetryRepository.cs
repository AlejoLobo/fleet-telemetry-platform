using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Mocks;

public class MockTelemetryRepository : ITelemetryRepository
{
    private readonly ILogger<MockTelemetryRepository> _logger;

    public MockTelemetryRepository(ILogger<MockTelemetryRepository> logger)
    {
        _logger = logger;
    }

    public Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MOCK] SaveAsync called for event {EventId} (API profile does not persist; Worker handles storage)",
            telemetryEvent.EventId);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TelemetryEvent>> GetByVehicleAsync(
        string vehicleId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MOCK] GetByVehicleAsync called for vehicle {VehicleId} (returning empty list)",
            vehicleId);

        IReadOnlyList<TelemetryEvent> result = Array.Empty<TelemetryEvent>();
        return Task.FromResult(result);
    }
}
