using System.Text;
using FleetTelemetry.Infrastructure;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// Punto de entrada de la API REST de telemetría de flota.
var builder = WebApplication.CreateBuilder(args);

ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

// Controladores y documentación OpenAPI.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
// JWT solo si la autenticación está habilitada en configuración.
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
    builder.Services.AddAuthorization();
}

builder.Services.AddInfrastructure(builder.Configuration, InfrastructureProfile.Api);
builder.Services.AddFleetSsePolling();

var app = builder.Build();

app.UseCors();

if (authOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();
