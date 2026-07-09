using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

/// <summary>
/// Agente híbrido: consulta tools operativas y opcionalmente pule la respuesta con OpenAI.
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

        if (!_openAiOptions.Enabled)
            return response;

        try
        {
            var polished = await _polish.PolishAsync(request.Question, response.Answer, cancellationToken);
            return response with { Answer = polished };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo pulir respuesta con LLM; se devuelve respuesta operativa");
            return response;
        }
    }
}
