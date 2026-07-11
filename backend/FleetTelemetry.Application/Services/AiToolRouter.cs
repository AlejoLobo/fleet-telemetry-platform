using Microsoft.Extensions.Logging;

// Enrutador determinista de intenciones a herramientas operativas.
namespace FleetTelemetry.Application.Services;

// Resultado del enrutamiento a una herramienta.
public sealed record AiToolRoutingResult(
    bool Success,
    string? ToolName,
    string Answer,
    IReadOnlyList<string> Sources,
    string? RejectionReason);

// Valida parámetros, aplica timeout y reduce resultados según el catálogo.
public class AiToolRouter
{
    private const double DefaultSpeedKmh = 80;
    private const int DefaultStoppedMinutes = 20;

    private readonly AiOperationalTools _tools;
    private readonly ILogger<AiToolRouter> _logger;

    public AiToolRouter(AiOperationalTools tools, ILogger<AiToolRouter> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    // Enruta la intención parseada a la herramienta correspondiente.
    public async Task<AiToolRoutingResult> RouteAsync(
        AiQuestionIntent intent,
        CancellationToken cancellationToken = default)
    {
        if (intent.Intent == AiQueryIntent.UnsupportedQuery)
        {
            return Reject(
                null,
                "unsupported_query",
                "No puedo responder esa consulta. Solo atiendo preguntas operativas sobre la flota " +
                "(estado, alertas, detenciones, velocidad y analítica).");
        }

        var toolName = MapIntentToTool(intent.Intent);
        if (!AiToolCatalog.TryGet(toolName, out var definition))
        {
            return Reject(
                toolName,
                "unsupported_tool",
                "La herramienta solicitada no está disponible en el catálogo operativo.");
        }

        var validationError = ValidateParameters(intent, definition);
        if (validationError is not null)
            return Reject(toolName, "invalid_parameters", validationError);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(definition.Timeout);

            var (answer, sources) = await InvokeToolAsync(intent, toolName, timeoutCts.Token);
            var reducedAnswer = ReduceResultLines(answer, definition.MaxResultLines);

            _logger.LogInformation(
                "Herramienta {Tool} ejecutada correctamente (fuentes: {Sources})",
                toolName,
                string.Join(", ", sources));

            return new(true, toolName, reducedAnswer, sources, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout al ejecutar herramienta {Tool} ({Timeout}s)", toolName, definition.Timeout.TotalSeconds);
            return Reject(
                toolName,
                "tool_timeout",
                "La consulta tardó demasiado. Intenta con un filtro más específico.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar herramienta {Tool}", toolName);
            return Reject(
                toolName,
                "tool_error",
                "No se pudo completar la consulta operativa. Intenta de nuevo en unos segundos.");
        }
    }

    private static string MapIntentToTool(AiQueryIntent intent) =>
        intent switch
        {
            AiQueryIntent.FleetOverview => AiToolCatalog.GetFleetOverview,
            AiQueryIntent.VehicleStatus => AiToolCatalog.GetLatestVehicleStatus,
            AiQueryIntent.CriticalAlerts => AiToolCatalog.GetVehiclesWithCriticalAlerts,
            AiQueryIntent.StoppedVehicles => AiToolCatalog.GetStoppedVehicles,
            AiQueryIntent.StoppedLongerThan => AiToolCatalog.GetVehiclesStoppedLongerThan,
            AiQueryIntent.StoppedInCriticalZones => AiToolCatalog.GetVehiclesStoppedLongerThan,
            AiQueryIntent.SpeedAbove => AiToolCatalog.GetVehiclesAboveSpeed,
            AiQueryIntent.AnalyticsSummary => AiToolCatalog.GetAnalyticsSummary,
            _ => string.Empty
        };

    private static string? ValidateParameters(AiQuestionIntent intent, AiToolDefinition definition)
    {
        foreach (var parameter in definition.Parameters.Where(p => p.Required))
        {
            var value = GetParameterValue(intent, parameter.Name);
            if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
                return $"Falta el parámetro obligatorio '{parameter.Name}' para {definition.Name}.";
        }

        if (intent.Intent == AiQueryIntent.SpeedAbove)
        {
            var threshold = intent.SpeedThresholdKmh;
            var speedParam = definition.Parameters.First(p => p.Name == "thresholdKmh");
            if (threshold is null
                || threshold < speedParam.Minimum
                || threshold > speedParam.Maximum)
                return $"El umbral de velocidad debe estar entre {speedParam.Minimum} y {speedParam.Maximum} km/h.";
        }

        if (intent.Intent is AiQueryIntent.StoppedLongerThan or AiQueryIntent.StoppedInCriticalZones)
        {
            var minutes = intent.StoppedMinutes ?? DefaultStoppedMinutes;
            var minutesParam = definition.Parameters.First(p => p.Name == "minutes");
            if (minutes < minutesParam.Minimum || minutes > minutesParam.Maximum)
                return $"Los minutos de detención deben estar entre {minutesParam.Minimum} y {minutesParam.Maximum}.";
        }

        if (intent.Intent == AiQueryIntent.VehicleStatus && intent.VehicleId is null)
            return "Se requiere un identificador de vehículo (ej. VH-001).";

        return null;
    }

    private static object? GetParameterValue(AiQuestionIntent intent, string parameterName) =>
        parameterName switch
        {
            "vehicleId" => intent.VehicleId,
            "minutes" => intent.StoppedMinutes,
            "thresholdKmh" => intent.SpeedThresholdKmh,
            "criticalZonesOnly" => intent.CriticalZonesOnly,
            "zoneName" => intent.ZoneName,
            _ => null
        };

    private Task<(string Answer, IReadOnlyList<string> Sources)> InvokeToolAsync(
        AiQuestionIntent intent,
        string toolName,
        CancellationToken cancellationToken) =>
        toolName switch
        {
            AiToolCatalog.GetStoppedVehicles =>
                _tools.GetStoppedVehiclesAsync(cancellationToken),

            AiToolCatalog.GetVehiclesStoppedLongerThan =>
                _tools.GetVehiclesStoppedLongerThanAsync(
                    intent.StoppedMinutes ?? DefaultStoppedMinutes,
                    intent.Intent == AiQueryIntent.StoppedInCriticalZones || intent.CriticalZonesOnly,
                    intent.ZoneName,
                    cancellationToken),

            AiToolCatalog.GetVehiclesWithCriticalAlerts =>
                _tools.GetVehiclesWithCriticalAlertsAsync(cancellationToken),

            AiToolCatalog.GetLatestVehicleStatus =>
                _tools.GetLatestVehicleStatusAsync(intent.VehicleId!, cancellationToken),

            AiToolCatalog.GetVehiclesAboveSpeed =>
                _tools.GetVehiclesAboveSpeedAsync(intent.SpeedThresholdKmh ?? DefaultSpeedKmh, cancellationToken),

            AiToolCatalog.GetAnalyticsSummary =>
                _tools.GetAnalyticsSummaryAsync(intent.VehicleId, cancellationToken),

            AiToolCatalog.GetFleetOverview =>
                _tools.GetFleetOverviewAsync(cancellationToken),

            _ => Task.FromResult<(string, IReadOnlyList<string>)>(
                ("Herramienta no soportada.", ["unsupported_tool"]))
        };

    // Trunca listas largas conservando el encabezado y un aviso de recorte.
    public static string ReduceResultLines(string answer, int maxLines)
    {
        if (maxLines <= 0)
            return answer;

        var lines = answer.Split('\n');
        if (lines.Length <= maxLines)
            return answer;

        var header = lines[0];
        var itemLines = lines.Skip(1).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();
        if (itemLines.Count == 0)
            return string.Join('\n', lines.Take(maxLines));

        var kept = itemLines.Take(maxLines - 1).ToList();
        var omitted = itemLines.Count - kept.Count;
        kept.Add($"- … y {omitted} registro(s) más (resultado reducido por límite operativo).");

        return string.Join('\n', new[] { header }.Concat(kept));
    }

    private static AiToolRoutingResult Reject(string? toolName, string reason, string message) =>
        new(false, toolName, message, [reason], reason);
}
