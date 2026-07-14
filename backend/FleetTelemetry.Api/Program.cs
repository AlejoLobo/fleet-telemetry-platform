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
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Demasiadas solicitudes. Intente de nuevo más tarde.",
                retryAfterSeconds = retryAfter.TotalSeconds
            }, cancellationToken);
        };

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
                    QueueLimit = rateLimitOptions.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));
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

public partial class Program;
