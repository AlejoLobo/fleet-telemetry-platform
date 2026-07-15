using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FleetTelemetry.Api.Exceptions;
using FleetTelemetry.Api.Middleware;
using FleetTelemetry.Infrastructure;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFleetOpenTelemetryLogging(builder.Configuration, InfrastructureProfile.Api);

// Valida secretos al arrancar (Auth, TimescaleDB, OpenAI) antes de registrar servicios.
ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.Configure<CorsPolicyOptions>(builder.Configuration.GetSection(CorsPolicyOptions.SectionName));
var corsOptions = builder.Configuration.GetSection(CorsPolicyOptions.SectionName).Get<CorsPolicyOptions>()
    ?? new CorsPolicyOptions();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
            policy.WithOrigins(corsOptions.AllowedOrigins);

        if (corsOptions.AllowAnyHeader)
            policy.AllowAnyHeader();

        if (corsOptions.AllowAnyMethod)
            policy.AllowAnyMethod();
    });
});

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection(RateLimitingOptions.SectionName));

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
// JWT y políticas solo si la autenticación está habilitada en configuración.
if (authOptions.Enabled)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = authOptions.JwtIssuer,
                ValidAudience = authOptions.JwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSecret)),
            };
        });
    builder.Services.AddAuthorization(AuthorizationPolicyRegistrar.ConfigurePolicies);
}

var rateLimitOptions = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
    ?? new RateLimitingOptions();
if (rateLimitOptions.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            var isTelemetryIngest = IsTelemetryIngestRequest(context.HttpContext);
            var retryAfterSeconds = isTelemetryIngest
                ? rateLimitOptions.TelemetryWindowSeconds
                : rateLimitOptions.WindowSeconds;

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Demasiadas solicitudes. Intente de nuevo más tarde.",
                retryAfterSeconds
            }, cancellationToken);
        };

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            if (httpContext.Request.Path.StartsWithSegments("/health"))
                return RateLimitPartition.GetNoLimiter("health");

            if (httpContext.Request.Path.StartsWithSegments("/api/events/stream"))
                return RateLimitPartition.GetNoLimiter("sse");

            if (IsTelemetryIngestRequest(httpContext))
            {
                var partitionKey = ResolveTelemetryPartitionKey(httpContext);
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOptions.TelemetryPermitLimit,
                        Window = TimeSpan.FromSeconds(rateLimitOptions.TelemetryWindowSeconds),
                        QueueLimit = rateLimitOptions.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
                    QueueLimit = rateLimitOptions.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        });
    });
}

builder.Services.AddInfrastructure(builder.Configuration, InfrastructureProfile.Api);
builder.Services.AddFleetSseDelivery(builder.Configuration);
builder.Services.AddFleetOpenTelemetry(builder.Configuration, InfrastructureProfile.Api);

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors();

if (authOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (rateLimitOptions.Enabled)
    app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

static bool IsTelemetryIngestRequest(HttpContext context)
{
    if (!HttpMethods.IsPost(context.Request.Method))
        return false;

    var path = context.Request.Path;
    // Coincidencia exacta: no aplicar cuota de ingesta a /api/telemetry/batch/admin ni batch-test.
    return path.Equals("/api/telemetry", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/api/telemetry/batch", StringComparison.OrdinalIgnoreCase);
}

static bool TryNormalizeDeviceId(string? raw, out string normalized)
{
    normalized = string.Empty;
    if (string.IsNullOrWhiteSpace(raw))
        return false;

    var trimmed = raw.Trim();
    if (trimmed.Contains('\n') || trimmed.Contains('\r'))
        return false;

    // Normaliza UUIDs a formato D para particionar la misma identidad con distintos casing/formatos.
    if (Guid.TryParse(trimmed, out var deviceGuid) && deviceGuid != Guid.Empty)
    {
        normalized = deviceGuid.ToString("D");
        return true;
    }

    if (trimmed.Length is < 8 or > 128)
        return false;

    foreach (var ch in trimmed)
    {
        var allowed = char.IsAsciiLetterOrDigit(ch)
            || ch is '-' or '_' or '.' or ':';
        if (!allowed)
            return false;
    }

    normalized = trimmed;
    return true;
}

static string ResolveTelemetryPartitionKey(HttpContext httpContext)
{
    var deviceClaim = httpContext.User.FindFirstValue("device_id");
    if (TryNormalizeDeviceId(deviceClaim, out var normalizedClaim))
        return $"device:{normalizedClaim}";

    if (httpContext.Request.Headers.TryGetValue("X-Device-Id", out var deviceHeader)
        && TryNormalizeDeviceId(deviceHeader.ToString(), out var normalizedHeader))
        return $"device:{normalizedHeader}";

    var subject = httpContext.User.FindFirstValue("sub")
        ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrWhiteSpace(subject))
        return $"user:{subject.Trim()}";

    return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

public partial class Program;
