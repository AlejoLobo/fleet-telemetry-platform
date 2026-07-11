using System.Security.Claims;

namespace FleetTelemetry.Application.Interfaces;

// Emite y valida tickets efímeros para conexiones SSE (EventSource no admite Authorization).
public interface ISseStreamTicketService
{
    string IssueTicket(ClaimsPrincipal user);

    bool TryValidate(string ticket, out ClaimsPrincipal? principal);
}
