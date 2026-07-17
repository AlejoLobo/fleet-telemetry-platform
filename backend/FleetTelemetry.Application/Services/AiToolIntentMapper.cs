using System.Text.Json;

namespace FleetTelemetry.Application.Services;

public static class AiToolIntentMapper
{
    public static AiQuestionIntent FromToolCall(string toolName, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        if (!AiToolCatalog.IsSupported(toolName))
            return AiQuestionIntent.Unsupported();

        return toolName switch
        {
            AiToolCatalog.GetFleetOverview => AiQuestionIntent.FleetOverview(),
            AiToolCatalog.GetStoppedVehicles => AiQuestionIntent.StoppedVehicles(),
            AiToolCatalog.GetVehiclesWithCriticalAlerts => AiQuestionIntent.CriticalAlerts(),
            AiToolCatalog.GetLatestVehicleStatus => AiQuestionIntent.VehicleStatus(
                GetGuid(arguments, "deviceId") ?? Guid.Empty),
            AiToolCatalog.GetVehiclesAboveSpeed => AiQuestionIntent.SpeedAbove(
                GetDouble(arguments, "thresholdKmh") ?? 80),
            AiToolCatalog.GetAnalyticsSummary => AiQuestionIntent.Analytics(
                GetGuid(arguments, "deviceId")),
            AiToolCatalog.GetVehiclesStoppedLongerThan => AiQuestionIntent.StoppedLongerThan(
                (int)(GetDouble(arguments, "minutes") ?? 20),
                GetString(arguments, "zoneName"),
                GetBool(arguments, "criticalZonesOnly")),
            _ => AiQuestionIntent.Unsupported()
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? GetGuid(IReadOnlyDictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var parsed)
            && parsed != Guid.Empty)
            return parsed;

        return null;
    }

    private static double? GetDouble(IReadOnlyDictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool GetBool(IReadOnlyDictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed;
    }
}
