namespace FleetTelemetry.Application.Configuration;

public class TelemetryIngestOptions
{
    public const string SectionName = "TelemetryIngest";

    public int MaxBatchSize { get; set; } = 100;
    public int MaxPayloadBytes { get; set; } = 16_384;
    public int MaxVehicleIdLength { get; set; } = 64;
    public int MaxDriverIdLength { get; set; } = 64;
    public int MaxFutureSkewMinutes { get; set; } = 5;
    public int MaxPastSkewDays { get; set; } = 30;
}
