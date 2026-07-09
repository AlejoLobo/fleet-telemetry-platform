using FleetTelemetry.Infrastructure;
using FleetTelemetry.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, InfrastructureProfile.Worker);
builder.Services.AddHostedService<TelemetryConsumerWorker>();

var host = builder.Build();
host.Run();
