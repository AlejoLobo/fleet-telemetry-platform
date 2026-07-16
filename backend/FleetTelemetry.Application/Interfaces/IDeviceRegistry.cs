using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.Interfaces;

/// <summary>
/// Registro idempotente de dispositivos, renombre y actualización de perfil.
/// </summary>
public interface IDeviceRegistry
{
    Task<FleetDevice> RegisterDeviceAsync(
        Guid deviceId,
        string? vehicleType = null,
        CancellationToken cancellationToken = default);

    Task<FleetDevice> RenameDeviceAsync(
        Guid deviceId,
        string vehicleName,
        CancellationToken cancellationToken = default);

    Task<FleetDevice> UpdateDeviceProfileAsync(
        Guid deviceId,
        string? vehicleName,
        string? vehicleType,
        CancellationToken cancellationToken = default);

    Task<FleetDevice?> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
