using FleetTelemetry.Infrastructure;
using FleetTelemetry.Worker;

// Punto de entrada del worker consumidor de Kafka.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, InfrastructureProfile.Worker);
builder.Services.AddHostedService<TelemetryConsumerWorker>();

var host = builder.Build();
host.Run();
