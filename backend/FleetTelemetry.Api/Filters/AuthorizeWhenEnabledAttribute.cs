using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FleetTelemetry.Api.Filters;

/// <summary>
/// Exige JWT solo cuando Auth:Enabled=true en configuración.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthorizeWhenEnabledAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var enabled = configuration.GetSection(AuthOptions.SectionName).GetValue<bool>("Enabled");
        if (!enabled)
            return;

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            context.Result = new UnauthorizedObjectResult(new { error = "Se requiere autenticación JWT." });
    }
}
