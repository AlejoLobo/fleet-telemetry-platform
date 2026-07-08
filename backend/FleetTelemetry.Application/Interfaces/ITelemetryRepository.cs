using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

public interface ITelemetryRepository
{
    Task SaveAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelemetryEvent>> GetByVehicleAsync(string vehicleId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
