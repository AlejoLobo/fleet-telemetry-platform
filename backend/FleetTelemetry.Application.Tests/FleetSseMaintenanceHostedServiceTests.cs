using FleetTelemetry.Application.Realtime;
using FleetTelemetry.Infrastructure;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

public class FleetSseMaintenanceHostedServiceTests
{
    [Fact]
    public void KafkaPush_registra_servicio_de_heartbeat()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sse:Mode"] = "KafkaPush",
                ["Sse:InstanceId"] = "test-replica",
                ["Kafka:BootstrapServers"] = "localhost:19092",
                ["Kafka:RealtimeTopic"] = "fleet.realtime",
                ["Kafka:RealtimeConsumerGroupBase"] = "fleet-realtime-sse",
            })
            .Build();

        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.Configure<SseOptions>(configuration.GetSection(SseOptions.SectionName));
        services.AddSingleton<IPostConfigureOptions<SseOptions>, SseOptionsPostConfigure>();
        services.AddSingleton<IValidateOptions<SseOptions>, SseOptionsValidator>();
        services.AddSingleton<FleetSseBroker>();
        services.AddFleetSseDelivery(configuration);

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();

        Assert.Contains(hosted, service => service is FleetSseMaintenanceHostedService);
        Assert.Contains(hosted, service => service is FleetSseKafkaPushHostedService);
        Assert.DoesNotContain(hosted, service => service is FleetSsePollerHostedService);
    }

    [Fact]
    public void Heartbeat_no_modifica_el_cursor()
    {
        var broker = new FleetSseBroker(TimeProvider.System);
        broker.PublishExternal(42, "alert", new { n = 1 });

        var service = CreateMaintenanceService(broker, heartbeatIntervalSeconds: 1);
        broker.SubscribeFrom(new SseLastEventId.Missing());

        service.PublishHeartbeatForTests();
        service.PublishHeartbeatForTests();

        Assert.Equal(42, broker.LatestStreamId);
    }

    [Fact]
    public async Task Heartbeat_mantiene_activo_al_suscriptor()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var broker = new FleetSseBroker(timeProvider);
        var subscription = broker.SubscribeFrom(new SseLastEventId.Missing());

        var service = CreateMaintenanceService(broker, timeProvider, heartbeatIntervalSeconds: 1);
        await service.RunMaintenanceCycleForTestsAsync();

        Assert.True(subscription.LiveReader.TryRead(out var heartbeat));
        Assert.Equal(FleetRealtimeEventTypes.Heartbeat, heartbeat.EventType);
        Assert.Equal(1, broker.SubscriberCount);
    }

    [Fact]
    public async Task Suscriptor_realmente_inactivo_se_elimina()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var broker = new FleetSseBroker(timeProvider);
        broker.SubscribeFrom(new SseLastEventId.Missing());

        timeProvider.Advance(TimeSpan.FromMinutes(31));
        var service = CreateMaintenanceService(broker, timeProvider);
        await service.RunMaintenanceCycleForTestsAsync();

        Assert.Equal(0, broker.SubscriberCount);
        Assert.Equal(1, broker.TotalUnsubscribes);
    }

    private static FleetSseMaintenanceHostedService CreateMaintenanceService(
        FleetSseBroker broker,
        TimeProvider? timeProvider = null,
        int heartbeatIntervalSeconds = 15) =>
        new(
            broker,
            Options.Create(new SseOptions
            {
                InstanceId = "maintenance-test",
                HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
            }),
            timeProvider ?? TimeProvider.System,
            NullLogger<FleetSseMaintenanceHostedService>.Instance);

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }
}

internal static class FleetSseMaintenanceHostedServiceTestExtensions
{
    internal static void PublishHeartbeatForTests(this FleetSseMaintenanceHostedService service)
    {
        var method = typeof(FleetSseMaintenanceHostedService).GetMethod(
            "PublishHeartbeatIfNeeded",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method!.Invoke(service, null);
    }

    internal static async Task RunMaintenanceCycleForTestsAsync(this FleetSseMaintenanceHostedService service)
    {
        var broker = (FleetSseBroker)typeof(FleetSseMaintenanceHostedService)
            .GetField("_broker", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(service)!;

        broker.PruneStaleSubscribers(CancellationToken.None);

        if (broker.SubscriberCount > 0)
            service.PublishHeartbeatForTests();

        await Task.CompletedTask;
    }
}
