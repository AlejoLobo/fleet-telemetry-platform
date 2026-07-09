namespace FleetTelemetry.Infrastructure.Configuration;

public class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}
