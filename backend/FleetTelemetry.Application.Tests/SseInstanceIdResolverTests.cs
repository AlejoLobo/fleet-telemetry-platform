using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

public class SseInstanceIdResolverTests
{
    [Fact]
    public void Configuracion_sin_InstanceId_usa_identidad_del_proceso()
    {
        var resolved = SseInstanceIdResolver.Resolve(null);
        Assert.False(string.IsNullOrWhiteSpace(resolved));

        var fromHostname = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrWhiteSpace(fromHostname))
            Assert.Equal(fromHostname.Trim(), resolved);
        else
            Assert.Equal(Environment.MachineName.Trim(), resolved);
    }

    [Fact]
    public void Dos_replicas_con_hostname_distinto_generan_grupos_distintos()
    {
        var readiness = new FleetKafkaPushReadiness();
        var serviceA = new FleetSseKafkaPushHostedService(
            new FleetSseBroker(TimeProvider.System),
            Options.Create(new KafkaOptions { RealtimeConsumerGroupBase = "fleet-realtime-sse" }),
            Options.Create(new SseOptions { InstanceId = "replica-host-a" }),
            readiness,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FleetSseKafkaPushHostedService>.Instance);

        var serviceB = new FleetSseKafkaPushHostedService(
            new FleetSseBroker(TimeProvider.System),
            Options.Create(new KafkaOptions { RealtimeConsumerGroupBase = "fleet-realtime-sse" }),
            Options.Create(new SseOptions { InstanceId = "replica-host-b" }),
            readiness,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FleetSseKafkaPushHostedService>.Instance);

        Assert.Equal("fleet-realtime-sse-replica-host-a", serviceA.ConsumerGroupId);
        Assert.Equal("fleet-realtime-sse-replica-host-b", serviceB.ConsumerGroupId);
        Assert.NotEqual(serviceA.ConsumerGroupId, serviceB.ConsumerGroupId);
    }

    [Fact]
    public void InstanceId_vacio_es_rechazado()
    {
        var validator = new SseOptionsValidator();
        var result = validator.Validate(null, new SseOptions { InstanceId = string.Empty });
        Assert.False(result.Succeeded);
        Assert.Contains("must not be empty", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void La_configuracion_distribuida_no_fija_el_mismo_id_para_todas_las_replicas()
    {
        var appsettingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FleetTelemetry.Api", "appsettings.json"));
        var composePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docker-compose.yml"));

        var appsettings = File.ReadAllText(appsettingsPath);
        var compose = File.ReadAllText(composePath);

        Assert.DoesNotContain("api-local", appsettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-docker", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InstanceId", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Sse__InstanceId", compose, StringComparison.OrdinalIgnoreCase);

        var replicaA = SseInstanceIdResolver.Resolve(null);
        var replicaB = SseInstanceIdResolver.Resolve("explicit-replica-b");
        Assert.NotEqual(replicaA, replicaB);
    }

    [Fact]
    public void PostConfigure_aplica_resolver_cuando_InstanceId_esta_vacio()
    {
        var postConfigure = new SseOptionsPostConfigure();
        var options = new SseOptions { InstanceId = string.Empty };
        postConfigure.PostConfigure(null, options);
        Assert.False(string.IsNullOrWhiteSpace(options.InstanceId));
    }
}
