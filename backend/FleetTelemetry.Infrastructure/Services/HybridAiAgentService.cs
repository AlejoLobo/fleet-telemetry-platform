using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

/// <summary>
/// Agente híbrido: consulta tools operativas de forma determinista y opcionalmente pule la respuesta con OpenAI.
/// Las consultas rechazadas o no soportadas nunca pasan por el LLM.
/// </summary>
public class HybridAiAgentService : IAiAgentService
{
    private readonly OperationalAiAgentService _operational;
    private readonly OpenAiPolishService _polish;
    private readonly OpenAiOptions _openAiOptions;
    private readonly ILogger<HybridAiAgentService> _logger;

    public HybridAiAgentService(
        OperationalAiAgentService operational,
        OpenAiPolishService polish,
        IOptions<OpenAiOptions> openAiOptions,
        ILogger<HybridAiAgentService> logger)
    {
        _operational = operational;
        _polish = polish;
        _openAiOptions = openAiOptions.Value;
        _logger = logger;
    }

    public async Task<AiQueryResponse> QueryAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _operational.QueryAsync(request, cancellationToken);

        if (!ShouldPolishWithLlm(request.Question, response, _openAiOptions.Enabled))
            return response;

        try
        {
            var polished = await _polish.PolishAsync(request.Question, response.Answer, cancellationToken);
            return response with { Answer = polished };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo pulir respuesta con LLM; se devuelve respuesta operativa determinista");
            return response;
        }
    }

    private static bool ShouldPolishWithLlm(string question, AiQueryResponse response, bool openAiEnabled)
    {
        if (!openAiEnabled)
            return false;

        if (response.Sources.Contains("prompt_guard", StringComparer.Ordinal)
            || response.Sources.Contains("unsupported_query", StringComparer.Ordinal)
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
