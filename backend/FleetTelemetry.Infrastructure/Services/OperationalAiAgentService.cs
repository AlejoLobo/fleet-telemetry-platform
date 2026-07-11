using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FleetTelemetry.Infrastructure.Services;

/// <summary>
/// Agente operativo sin LLM externo: sanitiza la pregunta, interpreta intención y enruta
/// al catálogo de herramientas internas mediante <see cref="AiToolRouter"/>.
/// </summary>
public class OperationalAiAgentService : IAiAgentService
{
    private readonly AiToolRouter _router;
    private readonly FleetTelemetryMetrics _metrics;
    private readonly ILogger<OperationalAiAgentService> _logger;

    public OperationalAiAgentService(
        AiToolRouter router,
        FleetTelemetryMetrics metrics,
        ILogger<OperationalAiAgentService> logger)
    {
        _router = router;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<AiQueryResponse> QueryAsync(AiQueryRequest request, CancellationToken cancellationToken = default)
    {
        var guard = AiPromptGuard.Inspect(request.Question);
        if (!guard.IsSafe)
        {
            _logger.LogWarning("Consulta IA rechazada por protección de prompt: {Reason}", guard.RejectionReason);
            return new AiQueryResponse(
                guard.RejectionReason ?? "Consulta no permitida.",
                ["prompt_guard"]);
        }

        var question = guard.SanitizedQuestion;
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

        var sw = Stopwatch.StartNew();
        var routing = await _router.RouteAsync(intent, cancellationToken);
        _metrics.RecordAiToolCall(routing.ToolName ?? "none", sw.Elapsed, routing.Success);
        var sources = routing.ToolName is not null
            ? routing.Sources.Prepend(routing.ToolName).Distinct(StringComparer.Ordinal).ToList()
            : routing.Sources;

        return new AiQueryResponse(AiResponseFormatter.LocalizarRespuesta(routing.Answer), sources);
    }
}
