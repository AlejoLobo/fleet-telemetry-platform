namespace FleetTelemetry.Infrastructure.Auth;

public static class AuthorizationPermissions
{
    public const string ClaimType = "permission";
    public const string DeviceIdClaimType = "device_id";

    public const string TelemetryWrite = "telemetry:write";
    public const string FleetRead = "fleet:read";
    public const string AlertAcknowledge = "alert:acknowledge";
    public const string AiQuery = "ai:query";
    public const string OperationsRead = "operations:read";
    public const string DeviceManage = "device:manage";

    /// <summary>
    /// Permisos de operador demo: lectura/ops/IA. Sin telemetry:write ni device:manage por defecto.
    /// </summary>
    public static readonly IReadOnlyList<string> OperatorPermissions =
    [
        FleetRead,
        AlertAcknowledge,
        AiQuery,
        OperationsRead
    ];
}
