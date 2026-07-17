namespace FleetTelemetry.Application.Configuration;

public sealed class AlertingOptions
{
    public const string SectionName = "Alerting";

    // Ventana mínima entre alertas emitidas del mismo DeviceId + AlertType.
    public int CooldownSeconds { get; set; } = 300;

    public double OverspeedThresholdKmh { get; set; } = 120;

    public double LowFuelPercent { get; set; } = 15;

    public double LowBatteryPercent { get; set; } = 20;
}
