using System.Text.RegularExpressions;

namespace FleetTelemetry.Application.Services;

public enum AiQueryIntent
{
    FleetOverview,
    VehicleStatus,
    CriticalAlerts,
    StoppedVehicles,
    StoppedLongerThan,
    StoppedInCriticalZones,
    SpeedAbove,
    AnalyticsSummary,
    UnsupportedQuery
}

public sealed record AiQuestionIntent(
    AiQueryIntent Intent,
    int? StoppedMinutes,
    double? SpeedThresholdKmh,
    Guid? DeviceId,
    string? ZoneName,
    bool CriticalZonesOnly)
{
    public static AiQuestionIntent FleetOverview() =>
        new(AiQueryIntent.FleetOverview, null, null, null, null, false);

    public static AiQuestionIntent VehicleStatus(Guid deviceId) =>
        new(AiQueryIntent.VehicleStatus, null, null, deviceId, null, false);

    public static AiQuestionIntent CriticalAlerts() =>
        new(AiQueryIntent.CriticalAlerts, null, null, null, null, false);

    public static AiQuestionIntent StoppedVehicles() =>
        new(AiQueryIntent.StoppedVehicles, null, null, null, null, false);

    public static AiQuestionIntent StoppedLongerThan(int minutes, string? zoneName, bool criticalOnly) =>
        new(
            criticalOnly || zoneName is not null
                ? AiQueryIntent.StoppedInCriticalZones
                : AiQueryIntent.StoppedLongerThan,
            minutes,
            null,
            null,
            zoneName,
            criticalOnly);

    public static AiQuestionIntent SpeedAbove(double thresholdKmh) =>
        new(AiQueryIntent.SpeedAbove, null, thresholdKmh, null, null, false);

    public static AiQuestionIntent Analytics(Guid? deviceId) =>
        new(AiQueryIntent.AnalyticsSummary, null, null, deviceId, null, false);

    public static AiQuestionIntent Unsupported() =>
        new(AiQueryIntent.UnsupportedQuery, null, null, null, null, false);
}

/// <summary>
/// Interpreta preguntas en lenguaje natural sin depender de un LLM externo.
/// </summary>
public static class AiQuestionParser
{
    private const int DefaultStoppedMinutes = 20;
    private const double DefaultSpeedKmh = 80;

    private static readonly Regex MinutesRegex = new(
        @"(\d+)\s*(?:min(?:utos?)?|mins?|m\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SpeedRegex = new(
        @"(\d+(?:[.,]\d+)?)\s*km\s*/?\s*h",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, int> SpanishNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cinco"] = 5,
        ["diez"] = 10,
        ["quince"] = 15,
        ["veinte"] = 20,
        ["treinta"] = 30,
        ["cuarenta"] = 40,
        ["cuarenta y cinco"] = 45,
        ["sesenta"] = 60,
    };

    public static AiQuestionIntent Parse(string question)
    {
        var text = question.Trim();
        var lower = text.ToLowerInvariant();
        var deviceId = AiOperationalTools.ExtractDeviceId(text);
        var minutes = ParseStoppedMinutes(lower);
        var speed = ParseSpeedThreshold(text);
        var zoneName = ExtractZoneName(lower);
        var mentionsCriticalZone = ContainsAny(lower,
            "zona critica", "zonas criticas", "zona crítica", "zonas críticas",
            "area critica", "área crítica", "sector critico", "sector crítico");
        var mentionsStopped = ContainsAny(lower,
            "deten", "parad", "stopped", "quieto", "inmovil", "inmóvil", "sin mover");
        var mentionsAlerts = ContainsAny(lower, "alerta", "alert");
        var mentionsCriticalSeverity = ContainsAny(lower, "crític", "critico", "critical", "grave");
        var mentionsSpeed = ContainsAny(lower, "veloc", "rápid", "rapido", "speed", "exceso");
        var mentionsAnalytics = ContainsAny(lower, "promedio", "analít", "analit", "analytics", "resumen anal");
        var mentionsStatus = ContainsAny(lower, "estado", "status", "vehículo", "vehiculo", "dispositivo", "device");
        var mentionsOverview = ContainsAny(lower, "resumen", "overview", "flota", "cuántos", "cuantos");

        if (mentionsStopped && (minutes.HasValue || mentionsCriticalZone || zoneName is not null))
        {
            var effectiveMinutes = minutes ?? DefaultStoppedMinutes;
            var criticalOnly = mentionsCriticalZone || zoneName is not null;
            return AiQuestionIntent.StoppedLongerThan(effectiveMinutes, zoneName, criticalOnly);
        }

        if (mentionsStopped)
            return AiQuestionIntent.StoppedVehicles();

        if (mentionsAlerts && mentionsCriticalSeverity)
            return AiQuestionIntent.CriticalAlerts();

        if (mentionsCriticalSeverity && !mentionsStopped)
            return AiQuestionIntent.CriticalAlerts();

        if (mentionsSpeed)
            return AiQuestionIntent.SpeedAbove(speed ?? DefaultSpeedKmh);

        if (mentionsAnalytics)
            return AiQuestionIntent.Analytics(deviceId);

        if (deviceId is not null && (mentionsStatus || mentionsStopped))
            return AiQuestionIntent.VehicleStatus(deviceId.Value);

        if (deviceId is not null)
            return AiQuestionIntent.VehicleStatus(deviceId.Value);

        if (mentionsAlerts)
            return AiQuestionIntent.CriticalAlerts();

        if (mentionsOverview)
            return AiQuestionIntent.FleetOverview();

        return AiQuestionIntent.Unsupported();
    }

    public static int? ParseStoppedMinutes(string lower)
    {
        var match = MinutesRegex.Match(lower);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed) && parsed > 0)
            return parsed;

        foreach (var (word, value) in SpanishNumbers.OrderByDescending(k => k.Key.Length))
        {
            if (lower.Contains(word, StringComparison.Ordinal))
                return value;
        }

        var bareNumber = Regex.Match(lower, @"\b(\d{1,3})\b");
        if (bareNumber.Success
            && int.TryParse(bareNumber.Groups[1].Value, out var minutes)
            && minutes >= 5
            && ContainsAny(lower, "deten", "parad", "stopped", "quieto", "min", "hora"))
        {
            if (lower.Contains("hora", StringComparison.Ordinal) && !lower.Contains("min", StringComparison.Ordinal))
                return minutes * 60;
            return minutes;
        }

        return null;
    }

    public static double? ParseSpeedThreshold(string question)
    {
        var match = SpeedRegex.Match(question);
        if (match.Success)
        {
            var raw = match.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var kmh) && kmh > 0)
                return kmh;
        }

        return AiOperationalTools.ParseSpeedThresholdOrNull(question);
    }

    public static string? ExtractZoneName(string lower)
    {
        foreach (var zone in CriticalZoneCatalog.All)
        {
            var normalized = Normalize(zone.Name);
            if (lower.Contains(normalized, StringComparison.Ordinal))
                return zone.Name;
        }

        var zoneMatch = Regex.Match(
            lower,
            @"(?:zona|area|área|sector|en)\s+([a-záéíóúñ\s]{3,20})",
            RegexOptions.IgnoreCase);
        if (zoneMatch.Success)
        {
            var candidate = zoneMatch.Groups[1].Value.Trim();
            return CriticalZoneCatalog.FindZoneByName(candidate)?.Name ?? candidate;
        }

        return null;
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.Ordinal));
}
