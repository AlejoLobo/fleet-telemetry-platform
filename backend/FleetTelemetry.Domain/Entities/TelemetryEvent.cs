using FleetTelemetry.Domain.ValueObjects;

namespace FleetTelemetry.Domain.Entities;

// Evento de telemetría con DeviceId como identidad estable del dispositivo.
public sealed class TelemetryEvent
{
    public Guid EventId => _eventId.Value;
    public Guid DeviceId => _deviceId.Value;
    public string? DriverId { get; }
    public DateTimeOffset Timestamp { get; }
    public double Latitude => _coordinate.Latitude;
    public double Longitude => _coordinate.Longitude;
    public double SpeedKmh => _speed.Value;
    public double? FuelLevelPercent => _fuelLevel?.Value;
    public double? BatteryPercent => _batteryLevel?.Value;
    public string LocationSource { get; }

    private readonly EventId _eventId;
    private readonly DeviceId _deviceId;
    private readonly GeoCoordinate _coordinate;
    private readonly SpeedKmh _speed;
    private readonly PercentLevel? _fuelLevel;
    private readonly PercentLevel? _batteryLevel;

    private TelemetryEvent(
        EventId eventId,
        DeviceId deviceId,
        string? driverId,
        DateTimeOffset timestamp,
        GeoCoordinate coordinate,
        SpeedKmh speed,
        PercentLevel? fuelLevel,
        PercentLevel? batteryLevel,
        string locationSource)
    {
        _eventId = eventId;
        _deviceId = deviceId;
        DriverId = string.IsNullOrWhiteSpace(driverId) ? null : driverId.Trim();
        Timestamp = timestamp;
        _coordinate = coordinate;
        _speed = speed;
        _fuelLevel = fuelLevel;
        _batteryLevel = batteryLevel;
        LocationSource = locationSource;
    }

    public static bool TryCreate(
        Guid eventId,
        Guid deviceId,
        string? driverId,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double speedKmh,
        double? fuelLevelPercent,
        double? batteryPercent,
        out TelemetryEvent? telemetryEvent,
        out string? error,
        string? locationSource = null)
    {
        telemetryEvent = null;
        error = null;

        if (!ValueObjects.EventId.TryCreate(eventId, out var domainEventId, out error))
            return false;

        if (!ValueObjects.DeviceId.TryCreate(deviceId, out var domainDeviceId, out error))
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

        telemetryEvent = new TelemetryEvent(
            domainEventId!,
            domainDeviceId!,
            driverId,
            timestamp,
            coordinate!,
            speed!,
            fuelLevel,
            batteryLevel,
            normalizedSource!);

        return true;
    }

    public static TelemetryEvent Create(
        Guid eventId,
        Guid deviceId,
        string? driverId,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double speedKmh,
        double? fuelLevelPercent = null,
        double? batteryPercent = null,
        string? locationSource = null) =>
        TryCreate(
            eventId,
            deviceId,
            driverId,
            timestamp,
            latitude,
            longitude,
            speedKmh,
            fuelLevelPercent,
            batteryPercent,
            out var telemetryEvent,
            out var error,
            locationSource)
            ? telemetryEvent!
            : throw new ArgumentException(error);

    public static TelemetryEvent FromPersistence(
        Guid eventId,
        Guid deviceId,
        string? driverId,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double speedKmh,
        double? fuelLevelPercent,
        double? batteryPercent,
        string? locationSource = null) =>
        new(
            ValueObjects.EventId.Create(eventId),
            ValueObjects.DeviceId.Create(deviceId),
            driverId,
            timestamp,
            GeoCoordinate.Create(latitude, longitude),
            ValueObjects.SpeedKmh.Create(speedKmh),
            PercentLevel.CreateOptional(fuelLevelPercent),
            PercentLevel.CreateOptional(batteryPercent),
            NormalizeLocationSource(locationSource));

    /// <summary>Compatibilidad temporal: columna "VehicleId" guarda Guid como string.</summary>
    public static TelemetryEvent FromPersistence(
        Guid eventId,
        string deviceIdStorage,
        string? driverId,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double speedKmh,
        double? fuelLevelPercent,
        double? batteryPercent,
        string? locationSource = null)
    {
        if (!Guid.TryParse(deviceIdStorage, out var deviceId))
            throw new ArgumentException("DeviceId storage value is not a valid Guid.", nameof(deviceIdStorage));

        return FromPersistence(
            eventId,
            deviceId,
            driverId,
            timestamp,
            latitude,
            longitude,
            speedKmh,
            fuelLevelPercent,
            batteryPercent,
            locationSource);
    }

    public string DeviceIdStorage => DeviceId.ToString("D");

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
