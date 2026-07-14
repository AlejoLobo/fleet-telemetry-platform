namespace FleetTelemetry.Infrastructure.Configuration;

// API key, modelo y URL base del servicio LLM.
public class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int MaxInputCharacters { get; set; } = 2_000;
    public int MaxToolCalls { get; set; } = 1;
    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}
