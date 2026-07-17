namespace FleetTelemetry.Infrastructure.Configuration;

public class CorsPolicyOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = ["http://localhost:3000"];
    public bool AllowAnyHeader { get; set; } = true;
    public bool AllowAnyMethod { get; set; } = true;
}
