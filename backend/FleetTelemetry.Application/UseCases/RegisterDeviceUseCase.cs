using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;

namespace FleetTelemetry.Application.UseCases;

public sealed class RegisterDeviceUseCase
{
    private readonly IDeviceRegistry _deviceRegistry;

    public RegisterDeviceUseCase(IDeviceRegistry deviceRegistry)
    {
        _deviceRegistry = deviceRegistry;
    }

    public Task<FleetDevice> ExecuteAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DeviceId == Guid.Empty)
            throw new InvalidDeviceIdException("DeviceId is required.");

        return _deviceRegistry.RegisterDeviceAsync(request.DeviceId, cancellationToken);
    }
}

public sealed class RenameDeviceUseCase
{
    private readonly IDeviceRegistry _deviceRegistry;

    public RenameDeviceUseCase(IDeviceRegistry deviceRegistry)
    {
        _deviceRegistry = deviceRegistry;
    }

    public Task<FleetDevice> ExecuteAsync(
        Guid deviceId,
        RenameDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            throw new InvalidDeviceIdException("DeviceId is required.");

        return _deviceRegistry.RenameDeviceAsync(deviceId, request.VehicleName, cancellationToken);
    }
}
