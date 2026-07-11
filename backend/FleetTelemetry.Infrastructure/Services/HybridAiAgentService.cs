using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

/// <summary>
/// Agente híbrido: intenta selección OpenAI con tool calls; si falla usa enrutador determinista.
/// </summary>
public class HybridAiAgentService : IAiAgentService
{
    private readonly OperationalAiAgentService _operational;
    private readonly OpenAiToolSelectionService _toolSelection;
    private readonly OpenAiPolishService _polish;
    private readonly OpenAiOptions _openAiOptions;
    private readonly ILogger<HybridAiAgentService> _logger;

    public HybridAiAgentService(
        OperationalAiAgentService operational,
        OpenAiToolSelectionService toolSelection,
        OpenAiPolishService polish,
        IOptions<OpenAiOptions> openAiOptions,
        ILogger<HybridAiAgentService> logger)
    {
        _operational = operational;
        _toolSelection = toolSelection;
        _polish = polish;
        _openAiOptions = openAiOptions.Value;
        _logger = logger;
    }

    public async Task<AiQueryResponse> QueryAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var guard = AiPromptGuard.Inspect(request.Question);
        if (!guard.IsSafe)
        {
            return new AiQueryResponse(
                guard.RejectionReason ?? "Consulta no permitida.",
                ["prompt_guard"]);
        }

        var question = guard.SanitizedQuestion;
        if (string.IsNullOrWhiteSpace(question))
            return new AiQueryResponse("Escribe una pregunta operativa sobre la flota.", ["validation"]);

        AiQueryResponse response;

        if (_openAiOptions.Enabled)
        {
            var llmResponse = await _toolSelection.TryQueryAsync(question, cancellationToken);
            if (llmResponse is not null)
            {
                response = llmResponse;
            }
            else
            {
                _logger.LogInformation("Fallback determinista para consulta IA");
                response = await _operational.QueryAsync(request with { Question = question }, cancellationToken);
            }
        }
        else
        {
            response = await _operational.QueryAsync(request with { Question = question }, cancellationToken);
        }

        if (!ShouldPolishWithLlm(question, response, _openAiOptions.Enabled))
            return response;

        try
        {
            var polished = await _polish.PolishAsync(question, response.Answer, cancellationToken);
            return response with { Answer = polished };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo pulir respuesta con LLM; se devuelve respuesta operativa");
            return response;
        }
    }

    private static bool ShouldPolishWithLlm(string question, AiQueryResponse response, bool openAiEnabled)
    {
        if (!openAiEnabled)
            return false;

        if (response.Sources.Contains("prompt_guard", StringComparer.Ordinal)
            || response.Sources.Contains("unsupported_query", StringComparer.Ordinal)
            || response.Sources.Contains("unsupported_tool", StringComparer.Ordinal)
            || response.Sources.Contains("validation", StringComparer.Ordinal))
        {
            return false;
        }

        var guard = AiPromptGuard.Inspect(question);
        if (!guard.IsSafe)
            return false;

        var intent = AiQuestionParser.Parse(guard.SanitizedQuestion);
        return intent.Intent != AiQueryIntent.UnsupportedQuery;
    }
}
