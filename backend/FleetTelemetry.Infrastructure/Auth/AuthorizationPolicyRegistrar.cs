using Microsoft.AspNetCore.Authorization;

namespace FleetTelemetry.Infrastructure.Auth;

// Configura políticas de autorización basadas en claims de permiso.
public static class AuthorizationPolicyRegistrar
{
    public static void ConfigurePolicies(AuthorizationOptions options)
    {
        options.AddPolicy(AuthorizationPolicies.TelemetryWrite, policy =>
            policy.RequireAuthenticatedUser()
                .RequireClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.TelemetryWrite));

        options.AddPolicy(AuthorizationPolicies.FleetRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.FleetRead));

        options.AddPolicy(AuthorizationPolicies.AlertAcknowledge, policy =>
            policy.RequireAuthenticatedUser()
                .RequireClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.AlertAcknowledge));

        options.AddPolicy(AuthorizationPolicies.AiQuery, policy =>
            policy.RequireAuthenticatedUser()
                .RequireClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.AiQuery));

        options.AddPolicy(AuthorizationPolicies.OperationsRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.OperationsRead));
    }
}
