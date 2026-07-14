using System.Text.Json;

// Catálogo explícito de herramientas operativas del agente IA.
namespace FleetTelemetry.Application.Services;

public sealed record AiToolParameterSchema(
    string Name,
    string Type,
    string Description,
    bool Required = false,
    JsonElement? Default = null,
    double? Minimum = null,
    double? Maximum = null);

public sealed record AiToolDefinition(
    string Name,
    string Description,
    IReadOnlyList<AiToolParameterSchema> Parameters,
    int MaxResultLines,
    TimeSpan Timeout);

// Catálogo inmutable de herramientas soportadas.
public static class AiToolCatalog
{
    public const string GetStoppedVehicles = "GetStoppedVehicles";
    public const string GetVehiclesStoppedLongerThan = "GetVehiclesStoppedLongerThan";
    public const string GetVehiclesWithCriticalAlerts = "GetVehiclesWithCriticalAlerts";
    public const string GetLatestVehicleStatus = "GetLatestVehicleStatus";
    public const string GetVehiclesAboveSpeed = "GetVehiclesAboveSpeed";
    public const string GetAnalyticsSummary = "GetAnalyticsSummary";
    public const string GetFleetOverview = "GetFleetOverview";

    private static readonly IReadOnlyDictionary<string, AiToolDefinition> Tools =
        new Dictionary<string, AiToolDefinition>(StringComparer.Ordinal)
        {
            [GetStoppedVehicles] = new(
                GetStoppedVehicles,
                "Lista vehículos detenidos según la última telemetría instantánea.",
                [],
                MaxResultLines: 50,
                Timeout: TimeSpan.FromSeconds(10)),

            [GetVehiclesStoppedLongerThan] = new(
                GetVehiclesStoppedLongerThan,
                "Lista vehículos detenidos más tiempo que un umbral en minutos, con filtros opcionales de zona.",
                [
                    new("minutes", "integer", "Minutos mínimos de detención.", Required: true, Minimum: 1, Maximum: 1440),
                    new("criticalZonesOnly", "boolean", "Filtrar solo zonas críticas.", Default: JsonSerializer.SerializeToElement(false)),
                    new("zoneName", "string", "Nombre de zona operativa específica.")
                ],
                MaxResultLines: 50,
                Timeout: TimeSpan.FromSeconds(15)),

            [GetVehiclesWithCriticalAlerts] = new(
                GetVehiclesWithCriticalAlerts,
                "Lista alertas abiertas con severidad crítica.",
                [],
                MaxResultLines: 50,
                Timeout: TimeSpan.FromSeconds(10)),

            [GetLatestVehicleStatus] = new(
                GetLatestVehicleStatus,
                "Estado reciente de un vehículo específico.",
                [
                    new("vehicleId", "string", "Identificador del vehículo (ej. VH-001).", Required: true)
                ],
                MaxResultLines: 10,
                Timeout: TimeSpan.FromSeconds(10)),

            [GetVehiclesAboveSpeed] = new(
                GetVehiclesAboveSpeed,
                "Lista vehículos cuya velocidad supera un umbral en km/h.",
                [
                    new("thresholdKmh", "number", "Umbral de velocidad en km/h.", Required: true, Minimum: 0, Maximum: 300)
                ],
                MaxResultLines: 50,
                Timeout: TimeSpan.FromSeconds(10)),

            [GetAnalyticsSummary] = new(
                GetAnalyticsSummary,
                "Resumen analítico de las últimas 24 horas para un vehículo.",
                [
                    new("vehicleId", "string", "Identificador del vehículo; si se omite se usa el primero disponible.")
                ],
                MaxResultLines: 10,
                Timeout: TimeSpan.FromSeconds(15)),

            [GetFleetOverview] = new(
                GetFleetOverview,
                "Panorama general de conectividad, alertas y detenciones prolongadas.",
                [],
                MaxResultLines: 20,
                Timeout: TimeSpan.FromSeconds(15))
        };

    public static IReadOnlyCollection<AiToolDefinition> All => Tools.Values.ToList();

    public static bool IsSupported(string toolName) =>
        Tools.ContainsKey(toolName);

    public static bool TryGet(string toolName, out AiToolDefinition definition) =>
        Tools.TryGetValue(toolName, out definition!);

    // Serializa metadatos del catálogo para inspección o documentación.
    public static string ToJsonMetadata()
    {
        var payload = All.Select(tool => new
        {
            tool.Name,
            tool.Description,
            parameters = tool.Parameters.Select(p => new
            {
                p.Name,
                p.Type,
                p.Description,
                p.Required,
                p.Default,
                p.Minimum,
                p.Maximum
            }),
            limits = new
            {
                tool.MaxResultLines,
                timeoutSeconds = tool.Timeout.TotalSeconds
            }
        });

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static IReadOnlyList<object> ToOpenAiToolDefinitions() =>
        All.Select(tool => new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = new
                {
                    type = "object",
                    properties = tool.Parameters.ToDictionary(
                        p => p.Name,
                        p => (object)new Dictionary<string, object?>
                        {
                            ["type"] = p.Type,
                            ["description"] = p.Description,
                            ["minimum"] = p.Minimum,
                            ["maximum"] = p.Maximum
                        }.Where(kv => kv.Value is not null)
                         .ToDictionary(kv => kv.Key, kv => kv.Value!)),
                    required = tool.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray()
                }
            }
        }).ToList();
}
