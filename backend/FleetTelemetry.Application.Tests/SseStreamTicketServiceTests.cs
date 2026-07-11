using System.Security.Claims;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

public class SseStreamTicketServiceTests
{
    private static readonly AuthOptions EnabledAuth = new()
    {
        Enabled = true,
        JwtSecret = "unit-test-secret-with-32-characters",
        DemoPassword = "admin123",
        SseTicketLifetimeSeconds = 60,
    };

    [Fact]
    public void IssueTicket_and_TryValidate_succeeds_for_authenticated_principal()
    {
        var service = CreateService();
        var principal = CreateOperatorPrincipal();

        var ticket = service.IssueTicket(principal);

        Assert.False(string.IsNullOrWhiteSpace(ticket));
        Assert.True(service.TryValidate(ticket, out var validated));
        Assert.NotNull(validated);
        Assert.True(validated!.Identity?.IsAuthenticated);
        Assert.Contains(
            validated.Claims,
            claim => claim.Type == AuthorizationPermissions.ClaimType
                && claim.Value == AuthorizationPermissions.FleetRead);
    }

    [Fact]
    public void TryValidate_rejects_missing_ticket()
    {
        var service = CreateService();

        Assert.False(service.TryValidate("", out var principal));
        Assert.Null(principal);
    }

    [Fact]
    public void TryValidate_rejects_unknown_ticket()
    {
        var service = CreateService();

        Assert.False(service.TryValidate("not-a-valid-ticket", out var principal));
        Assert.Null(principal);
    }

    [Fact]
    public void TryValidate_rejects_expired_ticket()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero));
        var service = CreateService(time);
        var ticket = service.IssueTicket(CreateOperatorPrincipal());

        time.Advance(TimeSpan.FromSeconds(EnabledAuth.SseTicketLifetimeSeconds + 1));

        Assert.False(service.TryValidate(ticket, out var principal));
        Assert.Null(principal);
    }

    private static SseStreamTicketService CreateService(FakeTimeProvider? timeProvider = null) =>
        new(Options.Create(EnabledAuth), timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow));

    private static ClaimsPrincipal CreateOperatorPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "admin"),
            new(ClaimTypes.Role, "operator"),
            new(AuthorizationPermissions.ClaimType, AuthorizationPermissions.FleetRead),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
