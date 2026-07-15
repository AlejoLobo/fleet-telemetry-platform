namespace FleetTelemetry.Application.DTOs;

public sealed record RegisterDeviceRequest(Guid DeviceId);

public sealed record RenameDeviceRequest(string VehicleName);

public sealed record DeviceResponse(Guid DeviceId, string VehicleName);
