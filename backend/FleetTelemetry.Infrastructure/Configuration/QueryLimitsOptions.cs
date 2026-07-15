namespace FleetTelemetry.Infrastructure.Configuration;

public class QueryLimitsOptions
{
    public const string SectionName = "QueryLimits";

    public int FleetDefaultPageSize { get; set; } = 100;

    public int FleetMaxPageSize { get; set; } = 500;

    public int HistoryDefaultPageSize { get; set; } = 200;

    public int HistoryMaxPageSize { get; set; } = 1000;

    public int HistoryMaxRangeDays { get; set; } = 7;

    public int OnlineThresholdMinutes { get; set; } = 1;

    /// <summary>
    /// Si es mayor que 0, reemplaza OnlineThresholdMinutes (útil en demos locales).
    /// </summary>
    public int OnlineThresholdSeconds { get; set; } = 0;

    public TimeSpan GetOnlineWindow() =>
        OnlineThresholdSeconds > 0
            ? TimeSpan.FromSeconds(OnlineThresholdSeconds)
            : TimeSpan.FromMinutes(OnlineThresholdMinutes);
}
