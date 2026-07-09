// Contrato de consultas analíticas sobre telemetría.
namespace FleetTelemetry.Application.Interfaces;

// Métricas agregadas por vehículo y rango temporal.
public interface IAnalyticsQueryService
{
    // Calcula velocidad promedio en un intervalo.
    Task<double> GetAverageSpeedAsync(string vehicleId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
