using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FleetTelemetry.Application.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FleetTelemetry.Application.Tests;

public class SseStreamAuthenticationIntegrationTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string JwtSecret = "integration-test-secret-with-32-chars-min";

    [Fact]
    public async Task Stream_without_auth_enabled_connects_without_ticket()
    {
        using var factory = CreateFactory(authEnabled: false);
        using var client = factory.CreateClient();

        await using var stream = await OpenSseStreamAsync(client, factory.GetStreamPath());
        var firstChunk = await ReadChunkAsync(stream, TimeSpan.FromSeconds(5));

        Assert.Contains("event: connected", firstChunk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_allows_authenticated_fleet_read_before_sse_ticket()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory);
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var fleet = await client.GetAsync("/api/fleet");
        Assert.Equal(HttpStatusCode.OK, fleet.StatusCode);
    }

    [Fact]
    public async Task Stream_with_auth_enabled_and_valid_ticket_connects()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ticket = await client.PostAsync("/api/events/stream/ticket", null);
        Assert.Equal(HttpStatusCode.OK, ticket.StatusCode);
        var ticketBody = await ticket.Content.ReadFromJsonAsync<SseStreamTicketResponse>(JsonOptions);
        Assert.NotNull(ticketBody);
        Assert.False(string.IsNullOrWhiteSpace(ticketBody!.Ticket));

        client.DefaultRequestHeaders.Authorization = null;
        await using var stream = await OpenSseStreamAsync(client, $"{factory.GetStreamPath()}?ticket={ticketBody.Ticket}");
        var firstChunk = await ReadChunkAsync(stream, TimeSpan.FromSeconds(5));

        Assert.Contains("event: connected", firstChunk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_with_auth_enabled_without_ticket_is_rejected()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(factory.GetStreamPath());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_with_auth_enabled_and_invalid_ticket_is_rejected()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{factory.GetStreamPath()}?ticket=invalid-ticket-value");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_with_auth_enabled_and_expired_ticket_is_rejected()
    {
        using var factory = CreateFactory(authEnabled: true, sseTicketLifetimeSeconds: 1);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ticket = await client.PostAsync("/api/events/stream/ticket", null);
        var ticketBody = await ticket.Content.ReadFromJsonAsync<SseStreamTicketResponse>(JsonOptions);
        Assert.NotNull(ticketBody);

        client.DefaultRequestHeaders.Authorization = null;
        await Task.Delay(TimeSpan.FromSeconds(2));

        var response = await client.GetAsync($"{factory.GetStreamPath()}?ticket={ticketBody!.Ticket}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ticket_endpoint_rejects_invalid_jwt()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        var response = await client.PostAsync("/api/events/stream/ticket", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reconnect_with_new_ticket_restores_authenticated_stream()
    {
        using var factory = CreateFactory(authEnabled: true);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        async Task<string> IssueTicketAsync()
        {
            var response = await client.PostAsync("/api/events/stream/ticket", null);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<SseStreamTicketResponse>(JsonOptions);
            return body!.Ticket;
        }

        var firstTicket = await IssueTicketAsync();
        client.DefaultRequestHeaders.Authorization = null;

        await using (var firstStream = await OpenSseStreamAsync(client, $"{factory.GetStreamPath()}?ticket={firstTicket}"))
        {
            var firstChunk = await ReadChunkAsync(firstStream, TimeSpan.FromSeconds(5));
            Assert.Contains("event: connected", firstChunk, StringComparison.Ordinal);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var secondTicket = await IssueTicketAsync();
        client.DefaultRequestHeaders.Authorization = null;

        await using var secondStream = await OpenSseStreamAsync(client, $"{factory.GetStreamPath()}?ticket={secondTicket}");
        var secondChunk = await ReadChunkAsync(secondStream, TimeSpan.FromSeconds(5));
        Assert.Contains("event: connected", secondChunk, StringComparison.Ordinal);
    }

    private static SseStreamAuthenticationWebApplicationFactory CreateFactory(
        bool authEnabled,
        int sseTicketLifetimeSeconds = 120) =>
        new()
        {
            AuthEnabled = authEnabled,
            SseTicketLifetimeSeconds = sseTicketLifetimeSeconds,
        };

    private static async Task<string> LoginAsync(HttpClient client, SseStreamAuthenticationWebApplicationFactory factory)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(factory.DemoUsername, factory.DemoPassword));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        return body!.Token;
    }

    private static async Task<Stream> OpenSseStreamAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStreamAsync();
    }

    private static async Task<string> ReadChunkAsync(Stream stream, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[2048];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}

public sealed class SseStreamAuthenticationWebApplicationFactory : WebApplicationFactory<Program>
{
    public bool AuthEnabled { get; init; } = true;
    public int SseTicketLifetimeSeconds { get; init; } = 120;

    public string DemoUsername => "admin";
    public string DemoPassword => "admin123";

    public string GetStreamPath() => "/api/events/stream";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Auth:Enabled", AuthEnabled.ToString());
        builder.UseSetting("Auth:JwtSecret", "integration-test-secret-with-32-chars-min");
        builder.UseSetting("Auth:JwtIssuer", "fleet-telemetry");
        builder.UseSetting("Auth:JwtAudience", "fleet-clients");
        builder.UseSetting("Auth:DemoUsername", DemoUsername);
        builder.UseSetting("Auth:DemoPassword", DemoPassword);
        builder.UseSetting("Auth:SseTicketLifetimeSeconds", SseTicketLifetimeSeconds.ToString());
        builder.UseSetting("TimescaleDb:ConnectionString", "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet");
        builder.UseSetting("Kafka:BootstrapServers", "localhost:19092");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = AuthEnabled.ToString(),
                ["Auth:JwtSecret"] = "integration-test-secret-with-32-chars-min",
                ["Auth:JwtIssuer"] = "fleet-telemetry",
                ["Auth:JwtAudience"] = "fleet-clients",
                ["Auth:DemoUsername"] = DemoUsername,
                ["Auth:DemoPassword"] = DemoPassword,
                ["Auth:SseTicketLifetimeSeconds"] = SseTicketLifetimeSeconds.ToString(),
                ["TimescaleDb:ConnectionString"] = "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet",
                ["Kafka:BootstrapServers"] = "localhost:19092",
                ["RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var descriptor in hosted)
                services.Remove(descriptor);
        });
    }
}
