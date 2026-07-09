// DTO de evento de telemetría para lectura.
namespace FleetTelemetry.Application.DTOs;

// Representación de un evento almacenado o consultado.
public record TelemetryEventResponse(
    Guid EventId,
    string VehicleId,
    string? DriverId,
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double SpeedKmh,
    double? FuelLevelPercent,
    double? BatteryPercent);
