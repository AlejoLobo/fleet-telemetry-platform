using Microsoft.AspNetCore.Authorization;

namespace FleetTelemetry.Infrastructure.Auth;

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

        options.AddPolicy(AuthorizationPolicies.DeviceManage, policy =>
            policy.RequireAuthenticatedUser()
                .RequireClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.DeviceManage));

        // Rename: token de dispositivo (telemetry:write) O operador con device:manage.
        options.AddPolicy(AuthorizationPolicies.DeviceRename, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.HasClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.TelemetryWrite)
                    || context.User.HasClaim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.DeviceManage)));
    }
}
