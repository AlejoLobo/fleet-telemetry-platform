using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

// Evento de telemetría vehicular con invariantes encapsulados.
public sealed class TelemetryEvent
{
    public Guid EventId => _eventId.Value;
    public string VehicleId => _vehicleId.Value;
    public string? DriverId { get; }
    public DateTimeOffset Timestamp { get; }
    public double Latitude => _coordinate.Latitude;
    public double Longitude => _coordinate.Longitude;
    public double SpeedKmh => _speed.Value;
    public double? FuelLevelPercent => _fuelLevel?.Value;
    public double? BatteryPercent => _batteryLevel?.Value;
    public string LocationSource { get; }
    public string? VehicleName { get; }

    private readonly EventId _eventId;
    private readonly VehicleId _vehicleId;
    private readonly GeoCoordinate _coordinate;
    private readonly SpeedKmh _speed;
    private readonly PercentLevel? _fuelLevel;
    private readonly PercentLevel? _batteryLevel;

    private TelemetryEvent(
        EventId eventId,
        VehicleId vehicleId,
        string? driverId,
        DateTimeOffset timestamp,
        GeoCoordinate coordinate,
        SpeedKmh speed,
        PercentLevel? fuelLevel,
        PercentLevel? batteryLevel,
        string locationSource,
        string? vehicleName)
    {
        _eventId = eventId;
        _vehicleId = vehicleId;
        DriverId = string.IsNullOrWhiteSpace(driverId) ? null : driverId.Trim();
        Timestamp = timestamp;
        _coordinate = coordinate;
        _speed = speed;
        _fuelLevel = fuelLevel;
        _batteryLevel = batteryLevel;
        LocationSource = locationSource;
        VehicleName = string.IsNullOrWhiteSpace(vehicleName) ? null : vehicleName.Trim();
        if (VehicleName is not null
            && string.Equals(VehicleName, _vehicleId.Value, StringComparison.OrdinalIgnoreCase))
        {
            // Evita persistir el ID de dispositivo como nombre visible.
            VehicleName = null;
        }
    }

    public static bool TryCreate(
        Guid eventId,
        string vehicleId,
        string? driverId,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double speedKmh,
        double? fuelLevelPercent,
        double? batteryPercent,
        out TelemetryEvent? telemetryEvent,
        out string? error,
        string? locationSource = null,
        string? vehicleName = null)
    {
        telemetryEvent = null;
        error = null;

        if (!ValueObjects.EventId.TryCreate(eventId, out var domainEventId, out error))
            return false;

        if (!ValueObjects.VehicleId.TryCreate(vehicleId, out var domainVehicleId, out error))
            return false;

        if (timestamp == default)
        {
            error = "Timestamp is required.";
            return false;
        }

        if (!GeoCoordinate.TryCreate(latitude, longitude, out var coordinate, out error))
            return false;

        if (!ValueObjects.SpeedKmh.TryCreate(speedKmh, out var speed, out error))
            return false;

        if (!PercentLevel.TryCreate(fuelLevelPercent, out var fuelLevel, out error))
            return false;

        if (!PercentLevel.TryCreate(batteryPercent, out var batteryLevel, out error))
            return false;

        if (!TryNormalizeLocationSource(locationSource, out var normalizedSource, out error))
            return false;

        if (!TryNormalizeVehicleName(vehicleName, out var normalizedName, out error))
            return false;

        telemetryEvent = new TelemetryEvent(
            domainEventId!,
            domainVehicleId!,
            driverId,
            timestamp,
            coordinate!,
            speed!,
            fuelLevel,
            batteryLevel,
            normalizedSource!,
            normalizedName);

        return true;
    }

    public static TelemetryEvent Create(
        Guid eventId,
        string vehicleId,
        string? driverId,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double speedKmh,
        double? fuelLevelPercent = null,
        double? batteryPercent = null,
        string? locationSource = null,
        string? vehicleName = null) =>
        TryCreate(
            eventId,
            vehicleId,
            driverId,
            timestamp,
            latitude,
            longitude,
            speedKmh,
            fuelLevelPercent,
            batteryPercent,
            out var telemetryEvent,
            out var error,
            locationSource,
            vehicleName)
            ? telemetryEvent!
            : throw new ArgumentException(error);

    public static TelemetryEvent FromPersistence(
        Guid eventId,
        string vehicleId,
        string? driverId,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double speedKmh,
        double? fuelLevelPercent,
        double? batteryPercent,
        string? locationSource = null,
        string? vehicleName = null) =>
        new(
            ValueObjects.EventId.Create(eventId),
            ValueObjects.VehicleId.Create(vehicleId),
            driverId,
            timestamp,
            GeoCoordinate.Create(latitude, longitude),
            ValueObjects.SpeedKmh.Create(speedKmh),
            PercentLevel.CreateOptional(fuelLevelPercent),
            PercentLevel.CreateOptional(batteryPercent),
            NormalizeLocationSource(locationSource),
            NormalizeVehicleName(vehicleName));

    private static bool TryNormalizeVehicleName(string? name, out string? normalized, out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            normalized = null;
            error = null;
            return true;
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 64)
        {
            normalized = null;
            error = "VehicleName must be at most 64 characters.";
            return false;
        }

        normalized = trimmed;
        error = null;
        return true;
    }

    private static string? NormalizeVehicleName(string? name) =>
        TryNormalizeVehicleName(name, out var normalized, out var error)
            ? normalized
            : throw new ArgumentException(error);

    private static bool TryNormalizeLocationSource(string? source, out string? normalized, out string? error)
    {
        normalized = string.IsNullOrWhiteSpace(source) ? "gps" : source.Trim().ToLowerInvariant();
        if (normalized is not ("gps" or "simulated"))
        {
            error = "LocationSource must be gps or simulated.";
            normalized = null;
            return false;
        }

        error = null;
        return true;
    }

    private static string NormalizeLocationSource(string? source) =>
        TryNormalizeLocationSource(source, out var normalized, out var error)
            ? normalized!
            : throw new ArgumentException(error);
}
