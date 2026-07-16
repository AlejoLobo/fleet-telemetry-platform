namespace FleetTelemetry.Application.DTOs;

public sealed record RegisterDeviceRequest(Guid DeviceId, string? VehicleType = null);

public sealed record RenameDeviceRequest(string VehicleName);

public sealed record UpdateDeviceProfileRequest(string? VehicleName = null, string? VehicleType = null);

public sealed record UpdateDeviceTypeRequest(string VehicleType);

public sealed record DeviceResponse(Guid DeviceId, string VehicleName, string VehicleType);
