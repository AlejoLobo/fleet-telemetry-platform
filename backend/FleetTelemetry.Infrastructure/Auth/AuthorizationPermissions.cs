namespace FleetTelemetry.Infrastructure.Auth;

// Claims de permiso incluidos en el JWT del operador.
public static class AuthorizationPermissions
{
    public const string ClaimType = "permission";

    public const string TelemetryWrite = "telemetry:write";
    public const string FleetRead = "fleet:read";
    public const string AlertAcknowledge = "alert:acknowledge";
    public const string AiQuery = "ai:query";
    public const string OperationsRead = "operations:read";

    // Permisos otorgados al rol operator en el MVP.
    public static readonly IReadOnlyList<string> OperatorPermissions =
    [
        TelemetryWrite,
        FleetRead,
        AlertAcknowledge,
        AiQuery,
        OperationsRead
    ];
}
