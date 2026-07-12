namespace FleetTelemetry.Infrastructure.Configuration;

// Límites de paginación y rangos de consulta histórica.
public class QueryLimitsOptions
{
    public const string SectionName = "QueryLimits";

    public int FleetDefaultPageSize { get; set; } = 100;

    public int FleetMaxPageSize { get; set; } = 500;

    public int HistoryDefaultPageSize { get; set; } = 200;

    public int HistoryMaxPageSize { get; set; } = 1000;

    public int HistoryMaxRangeDays { get; set; } = 7;

    public int OnlineThresholdMinutes { get; set; } = 5;
}
