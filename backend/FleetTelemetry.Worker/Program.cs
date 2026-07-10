using FleetTelemetry.Infrastructure;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Worker;

var builder = Host.CreateApplicationBuilder(args);

ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddInfrastructure(builder.Configuration, InfrastructureProfile.Worker);
builder.Services.AddSingleton<TelemetryMessageProcessor>();
builder.Services.AddHostedService<TelemetryConsumerWorker>();

var host = builder.Build();
host.Run();
