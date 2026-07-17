namespace FleetTelemetry.Application.Exceptions;

public sealed class DeviceNotFoundException : Exception
{
    public Guid DeviceId { get; }

    public DeviceNotFoundException(Guid deviceId)
        : base($"Device '{deviceId}' was not found.")
    {
        DeviceId = deviceId;
    }
}

public sealed class VehicleNameConflictException : Exception
{
    public string VehicleName { get; }

    public VehicleNameConflictException(string vehicleName)
        : base($"VehicleName '{vehicleName}' is already assigned to another device.")
    {
        VehicleName = vehicleName;
    }
}

public sealed class InvalidDeviceIdException : Exception
{
    public InvalidDeviceIdException(string message) : base(message)
    {
    }
}

public sealed class InvalidVehicleNameException : Exception
{
    public InvalidVehicleNameException(string message) : base(message)
    {
    }
}

public sealed class InvalidVehicleTypeException : Exception
{
    public InvalidVehicleTypeException(string message) : base(message)
    {
    }
}
