namespace FleetTelemetry.Infrastructure.Auth;

public static class AuthorizationPolicies
{
    public const string TelemetryWrite = "TelemetryWrite";
    public const string FleetRead = "FleetRead";
    public const string AlertAcknowledge = "AlertAcknowledge";
    public const string AiQuery = "AiQuery";
    public const string OperationsRead = "OperationsRead";
}
