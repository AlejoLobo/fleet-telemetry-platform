using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FleetTelemetry.Api.Filters;

/// <summary>
/// Autoriza el stream SSE con JWT (ticket endpoint) o ticket efímero en query string (EventSource).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SseStreamAuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var enabled = configuration.GetSection(AuthOptions.SectionName).GetValue<bool>("Enabled");
        if (!enabled)
            return;

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            var ticket = context.HttpContext.Request.Query["ticket"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(ticket))
            {
                var ticketService = context.HttpContext.RequestServices.GetRequiredService<ISseStreamTicketService>();
                if (ticketService.TryValidate(ticket, out var principal) && principal is not null)
                    context.HttpContext.User = principal;
            }
        }

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Se requiere autenticación JWT o ticket SSE válido." });
            return;
        }

        var authorizationService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var authorizationResult = await authorizationService.AuthorizeAsync(
            context.HttpContext.User,
            AuthorizationPolicies.FleetRead);
        if (!authorizationResult.Succeeded)
            context.Result = new ForbidResult();
    }
}
