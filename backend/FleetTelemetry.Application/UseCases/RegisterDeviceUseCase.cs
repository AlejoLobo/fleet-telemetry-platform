using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Domain.ValueObjects;

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

        // Tipo opcional: ausente → car. Si el dispositivo ya existe, el registro no modifica el tipo.
        string? vehicleType = null;
        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            if (!VehicleType.TryCreate(request.VehicleType, out var parsed, out var typeError))
                throw new InvalidVehicleTypeException(typeError ?? "VehicleType is invalid.");
            vehicleType = parsed!.Value;
        }

        return _deviceRegistry.RegisterDeviceAsync(request.DeviceId, vehicleType, cancellationToken);
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

        return _deviceRegistry.UpdateDeviceProfileAsync(
            deviceId,
            request.VehicleName,
            vehicleType: null,
            cancellationToken);
    }
}

public sealed class UpdateDeviceProfileUseCase
{
    private readonly IDeviceRegistry _deviceRegistry;

    public UpdateDeviceProfileUseCase(IDeviceRegistry deviceRegistry)
    {
        _deviceRegistry = deviceRegistry;
    }

    public Task<FleetDevice> ExecuteAsync(
        Guid deviceId,
        UpdateDeviceProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            throw new InvalidDeviceIdException("DeviceId is required.");

        if (string.IsNullOrWhiteSpace(request.VehicleName) && string.IsNullOrWhiteSpace(request.VehicleType))
            throw new InvalidVehicleNameException("At least one of vehicleName or vehicleType is required.");

        string? vehicleType = null;
        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            if (!VehicleType.TryCreate(request.VehicleType, out var parsed, out var typeError))
                throw new InvalidVehicleTypeException(typeError ?? "VehicleType is invalid.");
            vehicleType = parsed!.Value;
        }

        return _deviceRegistry.UpdateDeviceProfileAsync(
            deviceId,
            string.IsNullOrWhiteSpace(request.VehicleName) ? null : request.VehicleName,
            vehicleType,
            cancellationToken);
    }
}
