namespace FleetTelemetry.Infrastructure.Configuration;

// Umbrales configurables para detección de vehículos detenidos.
public class StoppedVehicleQueryOptions
{
    public const string SectionName = "StoppedVehicles";

    // Ventana histórica analizada por vehículo.
    public int LookbackHours { get; set; } = 48;

    // Máxima antigüedad de la última señal para considerar el vehículo vigente.
    public int MaxFreshnessMinutes { get; set; } = 30;

    // Hueco máximo tolerable entre eventos consecutivos en la secuencia detenida.
    public int MaxTelemetryGapSeconds { get; set; } = 600;

    // Velocidad por debajo de la cual se considera detenido.
    public double StoppedSpeedThresholdKmh { get; set; } = 1;
}
