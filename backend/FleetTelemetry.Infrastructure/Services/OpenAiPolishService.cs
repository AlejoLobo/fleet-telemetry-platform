using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace FleetTelemetry.Infrastructure.Services;

public class OpenAiPolishService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiPolishService> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _httpPipeline;

    public OpenAiPolishService(
        HttpClient httpClient,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiPolishService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpPipeline = ExternalDependencyResilience.CreateOpenAiHttpPipeline();
    }

    public async Task<string> PolishAsync(
        string question,
        string operationalAnswer,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return operationalAnswer;

        var response = await _httpPipeline.ExecuteAsync(
            async token =>
            {
                using var request = CreateChatRequest(question, operationalAnswer);
                return await _httpClient.SendAsync(request, token);
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken);
        var content = payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("OpenAI devolvió contenido vacío");
            return operationalAnswer;
        }

        return content;
    }

    private HttpRequestMessage CreateChatRequest(string question, string operationalAnswer)
    {
        var body = new
        {
            model = _options.Model,
            temperature = 0.2,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "Eres un asistente operativo de flotas. Reescribe la respuesta en español claro y profesional sin inventar datos. Mantén cifras y IDs exactos.",
                },
                new
                {
                    role = "user",
                    content = $"Pregunta: {question}\n\nRespuesta operativa:\n{operationalAnswer}",
                },
            },
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions");
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(body);
        return httpRequest;
    }

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
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
