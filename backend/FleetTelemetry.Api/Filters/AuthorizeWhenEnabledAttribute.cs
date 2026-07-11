using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

// Filtro de autorización condicional según configuración.
namespace FleetTelemetry.Api.Filters;

/// <summary>
/// Exige JWT y política opcional solo cuando Auth:Enabled=true en configuración.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthorizeWhenEnabledAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string? Policy { get; set; }

    public AuthorizeWhenEnabledAttribute()
    {
    }

    public AuthorizeWhenEnabledAttribute(string policy)
    {
        Policy = policy;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var enabled = configuration.GetSection(AuthOptions.SectionName).GetValue<bool>("Enabled");
        if (!enabled)
            return;

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Se requiere autenticación JWT." });
            return;
        }

        if (string.IsNullOrWhiteSpace(Policy))
            return;

        var authorizationService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var authorizationResult = await authorizationService.AuthorizeAsync(context.HttpContext.User, Policy);
        if (!authorizationResult.Succeeded)
            context.Result = new ForbidResult();
    }
}
