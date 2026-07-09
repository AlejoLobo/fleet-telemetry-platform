using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using Microsoft.Extensions.Logging;

// Agente IA operativo sin dependencia de LLM externo.
namespace FleetTelemetry.Infrastructure.Services;

/// <summary>
/// Agente operativo sin LLM externo: interpreta intención y enruta a herramientas internas.
/// La capa <see cref="HybridAiAgentService"/> puede pulir la respuesta con OpenAI si está configurado.
/// </summary>
// Parsea intención y delega a AiOperationalTools.
public class OperationalAiAgentService : IAiAgentService
{
    private readonly AiOperationalTools _tools;
    private readonly ILogger<OperationalAiAgentService> _logger;

    public OperationalAiAgentService(AiOperationalTools tools, ILogger<OperationalAiAgentService> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    // Interpreta pregunta y enruta a la herramienta correspondiente.
    public async Task<AiQueryResponse> QueryAsync(AiQueryRequest request, CancellationToken cancellationToken = default)
    {
        var question = request.Question.Trim();
        if (string.IsNullOrWhiteSpace(question))
            return new AiQueryResponse("Escribe una pregunta operativa sobre la flota.", ["validation"]);

        var intent = AiQuestionParser.Parse(question);
        _logger.LogInformation(
            "Consulta IA operativa: {Question} → {Intent} (min={Minutes}, zone={Zone}, critical={Critical})",
            question,
            intent.Intent,
            intent.StoppedMinutes,
            intent.ZoneName,
            intent.CriticalZonesOnly);

        var (answer, sources) = await DispatchAsync(intent, cancellationToken);
        return new AiQueryResponse(AiResponseFormatter.LocalizarRespuesta(answer), sources);
    }

    private Task<(string Answer, IReadOnlyList<string> Sources)> DispatchAsync(
        AiQuestionIntent intent,
        CancellationToken cancellationToken) =>
        intent.Intent switch
        {
            AiQueryIntent.StoppedInCriticalZones => _tools.GetVehiclesStoppedLongerThanAsync(
                intent.StoppedMinutes ?? 20,
                intent.CriticalZonesOnly,
                intent.ZoneName,
                cancellationToken),
            AiQueryIntent.StoppedLongerThan => _tools.GetVehiclesStoppedLongerThanAsync(
                intent.StoppedMinutes ?? 20,
                criticalZonesOnly: false,
                zoneName: null,
                cancellationToken),
            AiQueryIntent.StoppedVehicles => _tools.GetStoppedVehiclesAsync(cancellationToken),
            AiQueryIntent.CriticalAlerts => _tools.GetVehiclesWithCriticalAlertsAsync(cancellationToken),
            AiQueryIntent.SpeedAbove => _tools.GetVehiclesAboveSpeedAsync(
                intent.SpeedThresholdKmh ?? 80,
                cancellationToken),
            AiQueryIntent.AnalyticsSummary => _tools.GetAnalyticsSummaryAsync(intent.VehicleId, cancellationToken),
            AiQueryIntent.VehicleStatus when intent.VehicleId is not null =>
                _tools.GetLatestVehicleStatusAsync(intent.VehicleId, cancellationToken),
            AiQueryIntent.FleetOverview => _tools.GetFleetOverviewAsync(cancellationToken),
            _ => _tools.GetFleetOverviewAsync(cancellationToken)
        };
}
