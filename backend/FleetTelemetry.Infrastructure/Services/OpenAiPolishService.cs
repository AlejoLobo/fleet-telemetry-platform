using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

public class OpenAiPolishService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiPolishService> _logger;

    public OpenAiPolishService(
        HttpClient httpClient,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiPolishService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> PolishAsync(
        string question,
        string operationalAnswer,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return operationalAnswer;

        var request = new
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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions");
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(request);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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
