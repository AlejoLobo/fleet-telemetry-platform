using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Infrastructure.Services;

/// <summary>
/// Selecciona herramientas del catálogo cerrado mediante OpenAI function calling.
/// No permite SQL ni herramientas fuera de <see cref="AiToolCatalog"/>.
/// </summary>
public sealed class OpenAiToolSelectionService
{
    private readonly HttpClient _httpClient;
    private readonly AiToolRouter _router;
    private readonly OpenAiOptions _options;
    private readonly FleetTelemetryMetrics _metrics;
    private readonly ResiliencePipelineFactory _resilience;
    private readonly ILogger<OpenAiToolSelectionService> _logger;

    public OpenAiToolSelectionService(
        HttpClient httpClient,
        AiToolRouter router,
        IOptions<OpenAiOptions> options,
        FleetTelemetryMetrics metrics,
        ResiliencePipelineFactory resilience,
        ILogger<OpenAiToolSelectionService> logger)
    {
        _httpClient = httpClient;
        _router = router;
        _options = options.Value;
        _metrics = metrics;
        _resilience = resilience;
        _logger = logger;
    }

    public async Task<AiQueryResponse?> TryQueryAsync(
        string sanitizedQuestion,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        var question = Truncate(sanitizedQuestion, _options.MaxInputCharacters);

        try
        {
            var sw = Stopwatch.StartNew();
            var toolCall = await SelectToolCallAsync(question, cancellationToken);
            if (toolCall is null)
            {
                _logger.LogInformation("OpenAI no seleccionó herramienta del catálogo; se usará enrutador determinista");
                return null;
            }

            if (!AiToolCatalog.IsSupported(toolCall.Name))
            {
                return ToResponse(
                    AiToolRouter.ReduceResultLines(
                        "La herramienta solicitada no está disponible en el catálogo operativo.",
                        10),
                    toolCall.Name,
                    ["unsupported_tool", "openai_tool_call"]);
            }

            var routing = await _router.ExecuteToolCallAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
            _metrics.RecordAiToolCall(toolCall.Name, sw.Elapsed, routing.Success);

            var sources = routing.ToolName is not null
                ? routing.Sources.Prepend(routing.ToolName).Append("openai_tool_call").Distinct(StringComparer.Ordinal).ToList()
                : routing.Sources.Append("openai_tool_call").ToList();

            return ToResponse(AiResponseFormatter.LocalizarRespuesta(routing.Answer), routing.ToolName, sources);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "OpenAI bloqueado por circuit breaker; fallback determinista");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo en selección OpenAI; fallback determinista");
            return null;
        }
    }

    private async Task<SelectedToolCall?> SelectToolCallAsync(string question, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = _options.Model,
            temperature = 0,
            max_tokens = 256,
            tools = AiToolCatalog.ToOpenAiToolDefinitions(),
            tool_choice = "auto",
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "Eres un router operativo de telemetría de flota. " +
                        "Selecciona exactamente una herramienta del catálogo provisto. " +
                        "No inventes herramientas, no generes SQL ni consultes datos directamente. " +
                        "Si la pregunta no es operativa sobre la flota, no llames ninguna herramienta.",
                },
                new { role = "user", content = question },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(body);

        var response = await _resilience.OpenAiHttpPipeline.ExecuteAsync(
            async token => await _httpClient.SendAsync(request, token),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken);
        var toolCalls = payload?.Choices?.FirstOrDefault()?.Message?.ToolCalls;
        if (toolCalls is null || toolCalls.Count == 0)
            return null;

        var first = toolCalls.Take(Math.Max(1, _options.MaxToolCalls)).First();
        var args = ParseArguments(first.Function?.Arguments);
        if (first.Function?.Name is null)
            return null;

        return new SelectedToolCall(first.Function.Name, args);
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, JsonElement>();

        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars];

    private static AiQueryResponse ToResponse(string answer, string? toolName, IReadOnlyList<string> sources) =>
        new(answer, sources);

    private sealed record SelectedToolCall(string Name, IReadOnlyDictionary<string, JsonElement> Arguments);

    private sealed class OpenAiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public OpenAiMessage? Message { get; set; }
    }

    private sealed class OpenAiMessage
    {
        [JsonPropertyName("tool_calls")]
        public List<OpenAiToolCall>? ToolCalls { get; set; }
    }

    private sealed class OpenAiToolCall
    {
        [JsonPropertyName("function")]
        public OpenAiFunction? Function { get; set; }
    }

    private sealed class OpenAiFunction
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
    }
}
