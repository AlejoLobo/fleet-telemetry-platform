using System.Collections.Concurrent;
using System.Security.Claims;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

// Tickets efímeros en memoria; no exponen el JWT en la URL del stream.
public sealed class SseStreamTicketService : ISseStreamTicketService
{
    private readonly ConcurrentDictionary<string, TicketEntry> _tickets = new();
    private readonly AuthOptions _options;
    private readonly TimeProvider _timeProvider;

    public SseStreamTicketService(IOptions<AuthOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public string IssueTicket(ClaimsPrincipal user)
    {
        PurgeExpiredTickets();

        var ticket = Guid.NewGuid().ToString("N");
        var expiresAt = _timeProvider.GetUtcNow().AddSeconds(_options.SseTicketLifetimeSeconds);
        var principal = ClonePrincipal(user);

        _tickets[ticket] = new TicketEntry(principal, expiresAt);
        return ticket;
    }

    public bool TryValidate(string ticket, out ClaimsPrincipal? principal)
    {
        principal = null;
        if (string.IsNullOrWhiteSpace(ticket))
            return false;

        PurgeExpiredTickets();

        if (!_tickets.TryGetValue(ticket, out var entry))
            return false;

        if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            _tickets.TryRemove(ticket, out _);
            return false;
        }

        principal = ClonePrincipal(entry.Principal);
        return true;
    }

    private void PurgeExpiredTickets()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _tickets)
        {
            if (pair.Value.ExpiresAt <= now)
                _tickets.TryRemove(pair.Key, out _);
        }
    }

    private static ClaimsPrincipal ClonePrincipal(ClaimsPrincipal source)
    {
        var identity = source.Identity as ClaimsIdentity;
        var clonedIdentity = identity is null
            ? new ClaimsIdentity(source.Claims, source.Identity?.AuthenticationType)
            : new ClaimsIdentity(identity.Claims, identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType);

        return new ClaimsPrincipal(clonedIdentity);
    }

    private sealed record TicketEntry(ClaimsPrincipal Principal, DateTimeOffset ExpiresAt);
}
