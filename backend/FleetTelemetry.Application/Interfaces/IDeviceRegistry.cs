using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

/// <summary>
/// Registro idempotente de dispositivos y renombre de vehicleName.
/// </summary>
public interface IDeviceRegistry
{
    Task<FleetDevice> RegisterDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);

    Task<FleetDevice> RenameDeviceAsync(
        Guid deviceId,
        string vehicleName,
        CancellationToken cancellationToken = default);

    Task<FleetDevice?> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
