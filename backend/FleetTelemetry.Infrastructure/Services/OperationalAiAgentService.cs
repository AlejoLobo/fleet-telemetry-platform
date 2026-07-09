using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Infrastructure.Services;

/// <summary>
/// Agente operativo sin LLM externo: enruta preguntas a tools internas sobre Application Services.
/// Modo mock/controlado — no envía datasets al LLM.
/// </summary>
public class OperationalAiAgentService : IAiAgentService
{
    private readonly AiOperationalTools _tools;
    private readonly ILogger<OperationalAiAgentService> _logger;

    public OperationalAiAgentService(AiOperationalTools tools, ILogger<OperationalAiAgentService> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    public async Task<AiQueryResponse> QueryAsync(AiQueryRequest request, CancellationToken cancellationToken = default)
    {
        var question = request.Question.Trim();
        if (string.IsNullOrWhiteSpace(question))
            return new AiQueryResponse("Escribe una pregunta operativa sobre la flota.", ["validation"]);

        _logger.LogInformation("Consulta IA operativa: {Question}", question);

        var (answer, sources) = await RouteQuestionAsync(question, cancellationToken);
        return new AiQueryResponse(AiResponseFormatter.LocalizarRespuesta(answer), sources);
    }

    private async Task<(string Answer, IReadOnlyList<string> Sources)> RouteQuestionAsync(
        string question,
        CancellationToken cancellationToken)
    {
        var lower = question.ToLowerInvariant();

        if (ContainsAny(lower, "crític", "critico", "critical", "grave"))
            return await _tools.GetVehiclesWithCriticalAlertsAsync(cancellationToken);

        if (ContainsAny(lower, "deten", "parad", "stopped", "quieto"))
            return await _tools.GetStoppedVehiclesAsync(cancellationToken);

        if (ContainsAny(lower, "veloc", "rápid", "rapido", "speed", "exceso"))
        {
            var threshold = AiOperationalTools.ParseSpeedThreshold(question);
            return await _tools.GetVehiclesAboveSpeedAsync(threshold, cancellationToken);
        }

        if (ContainsAny(lower, "promedio", "analít", "analit", "analytics", "resumen anal"))
        {
            var vehicleId = AiOperationalTools.ExtractVehicleId(question);
            return await _tools.GetAnalyticsSummaryAsync(vehicleId, cancellationToken);
        }

        var extractedVehicle = AiOperationalTools.ExtractVehicleId(question);
        if (extractedVehicle is not null || ContainsAny(lower, "estado", "status", "vehículo", "vehiculo"))
        {
            if (extractedVehicle is not null)
                return await _tools.GetLatestVehicleStatusAsync(extractedVehicle, cancellationToken);
        }

        if (ContainsAny(lower, "alerta", "alert"))
        {
            var alerts = await _tools.GetVehiclesWithCriticalAlertsAsync(cancellationToken);
            return alerts;
        }

        return await _tools.GetFleetOverviewAsync(cancellationToken);
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.Ordinal));
}
