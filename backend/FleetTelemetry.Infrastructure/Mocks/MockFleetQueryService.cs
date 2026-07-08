using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Mocks;

public class MockFleetQueryService : IFleetQueryService
{
    private readonly ILogger<MockFleetQueryService> _logger;

    public MockFleetQueryService(ILogger<MockFleetQueryService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<VehicleLatestStatusResponse>> GetLatestVehicleStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] GetLatestVehicleStatusesAsync called");

        IReadOnlyList<VehicleLatestStatusResponse> result =
        [
            new VehicleLatestStatusResponse(
                VehicleId: "VH-001",
                Name: "Delivery Truck 01",
                Status: "online",
                LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-2),
                LastSpeedKmh: 42.5,
                LastLatitude: 4.6533,
                LastLongitude: -74.0836)
        ];

        return Task.FromResult(result);
    }

    public Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
        string vehicleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] GetVehicleStatusAsync called for vehicle {VehicleId}", vehicleId);

        if (vehicleId == "VH-001")
        {
            return Task.FromResult<VehicleLatestStatusResponse?>(
                new VehicleLatestStatusResponse(
                    VehicleId: "VH-001",
                    Name: "Delivery Truck 01",
                    Status: "online",
                    LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-2),
                    LastSpeedKmh: 42.5,
                    LastLatitude: 4.6533,
                    LastLongitude: -74.0836));
        }

        return Task.FromResult<VehicleLatestStatusResponse?>(null);
    }
}
