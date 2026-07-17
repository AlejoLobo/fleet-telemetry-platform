using FleetTelemetry.Infrastructure;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Observability;
using FleetTelemetry.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddFleetOpenTelemetryLogging(builder.Configuration, InfrastructureProfile.Worker);

ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddInfrastructure(builder.Configuration, InfrastructureProfile.Worker);
builder.Services.AddFleetOpenTelemetry(builder.Configuration, InfrastructureProfile.Worker);
builder.Services.AddFleetConnectivityExpiry();
builder.Services.AddSingleton<TelemetryMessageProcessor>();
builder.Services.AddSingleton<TelemetryMessageCoordinator>();
builder.Services.AddHostedService<TelemetryConsumerWorker>();

var host = builder.Build();
host.Run();
