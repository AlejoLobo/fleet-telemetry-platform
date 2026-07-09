using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Domain.Entities;

// Validación y mapeo de eventos de telemetría entrantes.
namespace FleetTelemetry.Application.Validation;

// Reglas de negocio para payloads de telemetría.
public static class TelemetryEventValidator
{
    // Valida campos obligatorios y rangos geográficos.
    public static void Validate(TelemetryEventRequest request)
    {
        if (request.EventId == Guid.Empty)
            throw new ArgumentException("EventId is required.");

        if (string.IsNullOrWhiteSpace(request.VehicleId))
            throw new ArgumentException("VehicleId is required.");

        if (request.Timestamp == default)
            throw new ArgumentException("Timestamp is required.");

        if (request.Latitude is < -90 or > 90)
            throw new ArgumentException("Latitude must be between -90 and 90.");

        if (request.Longitude is < -180 or > 180)
            throw new ArgumentException("Longitude must be between -180 and 180.");

        if (request.SpeedKmh < 0)
            throw new ArgumentException("SpeedKmh must be >= 0.");
    }

    // Convierte DTO de entrada a entidad de dominio.
    public static TelemetryEvent MapToDomain(TelemetryEventRequest request) => new()
    {
        EventId = request.EventId,
        VehicleId = request.VehicleId.Trim(),
        DriverId = request.DriverId?.Trim(),
        Timestamp = request.Timestamp,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        SpeedKmh = request.SpeedKmh,
        FuelLevelPercent = request.FuelLevelPercent,
        BatteryPercent = request.BatteryPercent
    };
}
