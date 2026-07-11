using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Repositories;
using FleetTelemetry.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FleetTelemetry.Integration.Tests;

// Host de prueba que ejecuta TelemetryConsumerWorker con dependencias reemplazables.
public sealed class TelemetryConsumerWorkerTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly CancellationTokenSource _runCts = new();

    public string BootstrapServers { get; }
    public string Topic { get; }
    public string DeadLetterTopic { get; }
    public string GroupId { get; }
    public bool UsesProductionDeadLetterPublisher { get; }
    public ControllableTelemetryProcessingUnitOfWork? Processing { get; }
    public ControllableDeadLetterPublisher? DeadLetterPublisher { get; }

    private TelemetryConsumerWorkerTestHost(
        IHost host,
        string bootstrapServers,
        string topic,
        string deadLetterTopic,
        string groupId,
        bool usesProductionDeadLetterPublisher,
        ControllableTelemetryProcessingUnitOfWork? processing,
        ControllableDeadLetterPublisher? deadLetterPublisher)
    {
        _host = host;
        BootstrapServers = bootstrapServers;
        Topic = topic;
        DeadLetterTopic = deadLetterTopic;
        GroupId = groupId;
        UsesProductionDeadLetterPublisher = usesProductionDeadLetterPublisher;
        Processing = processing;
        DeadLetterPublisher = deadLetterPublisher;
    }

    public static async Task<TelemetryConsumerWorkerTestHost> CreateAsync(
        KafkaIntegrationFixture kafka,
        string topicPrefix,
        string groupPrefix,
        TelemetryConsumerWorkerHostOptions? options = null)
    {
        options ??= new TelemetryConsumerWorkerHostOptions();

        var topic = options.ExistingTopic ?? kafka.NewTopicName(topicPrefix);
        var deadLetterTopic = options.ExistingDeadLetterTopic ?? kafka.NewTopicName($"{topicPrefix}-dlq");
        var groupId = options.ExistingGroupId ?? kafka.NewGroupId(groupPrefix);

        if (options.ExistingTopic is null)
            await kafka.CreateTopicAsync(topic, partitions: 1);

        if (options.ExistingDeadLetterTopic is null)
            await kafka.CreateTopicAsync(deadLetterTopic, partitions: 1);

        ControllableTelemetryProcessingUnitOfWork? processing = null;
        if (!options.UseRealTimescaleProcessing)
            processing = new ControllableTelemetryProcessingUnitOfWork();

        ControllableDeadLetterPublisher? deadLetterPublisher = null;
        if (!options.UseProductionDeadLetterPublisher)
        {
            deadLetterPublisher = new ControllableDeadLetterPublisher();
            options.ConfigureDeadLetterPublisher?.Invoke(deadLetterPublisher);
        }

        var connectionString = options.ConnectionString
            ?? "Host=localhost;Port=5432;Database=fleet;Username=fleet;Password=fleet";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = kafka.BootstrapServers,
                ["Kafka:TelemetryTopic"] = topic,
                ["Kafka:DeadLetterTopic"] = deadLetterTopic,
                ["Kafka:ConsumerGroup"] = groupId,
                ["Kafka:MaxProcessingAttempts"] = "3",
                ["Kafka:RetryInitialDelayMilliseconds"] = "50",
                ["Kafka:RetryMaxDelayMilliseconds"] = "200",
                ["Kafka:MaxDeadLetterPublishAttempts"] = "3",
                ["Kafka:MaxPollIntervalMilliseconds"] = "300000",
                ["TimescaleDb:ConnectionString"] = connectionString
            })
            .Build();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(config);
        builder.Logging.ClearProviders();

        FleetTelemetry.Infrastructure.DependencyInjection.AddInfrastructure(
            builder.Services,
            builder.Configuration,
            FleetTelemetry.Infrastructure.InfrastructureProfile.Worker);

        builder.Services.RemoveAll<ITelemetryProcessingUnitOfWork>();

        if (options.UseRealTimescaleProcessing)
        {
            builder.Services.AddScoped<ITelemetryProcessingUnitOfWork, TimescaleTelemetryProcessingUnitOfWork>();
        }
        else
        {
            builder.Services.AddSingleton(processing!);
            builder.Services.AddSingleton<ITelemetryProcessingUnitOfWork>(sp => sp.GetRequiredService<ControllableTelemetryProcessingUnitOfWork>());
        }

        if (!options.UseProductionDeadLetterPublisher)
        {
            builder.Services.RemoveAll<IDeadLetterPublisher>();
            builder.Services.AddSingleton<IDeadLetterPublisher>(deadLetterPublisher!);
        }

        builder.Services.AddSingleton<TelemetryMessageProcessor>();
        builder.Services.AddSingleton<TelemetryMessageCoordinator>();
        builder.Services.AddHostedService<TelemetryConsumerWorker>();

        if (options.ConfigureKafka is not null)
            builder.Services.PostConfigure(options.ConfigureKafka);

        var host = builder.Build();

        return new TelemetryConsumerWorkerTestHost(
            host,
            kafka.BootstrapServers,
            topic,
            deadLetterTopic,
            groupId,
            options.UseProductionDeadLetterPublisher,
            processing,
            deadLetterPublisher);
    }

    public async Task StartAsync()
    {
        await _host.StartAsync(_runCts.Token);
        await KafkaTestPolling.WaitUntilConsumerAssignedAsync(
            BootstrapServers,
            GroupId,
            Topic,
            TimeSpan.FromSeconds(30),
            _runCts.Token);
    }

    public async Task StopAsync()
    {
        _runCts.Cancel();
        await _host.StopAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        _runCts.Cancel();
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        _runCts.Dispose();
        DeadLetterPublisher?.Dispose();
    }
}

public sealed class TelemetryConsumerWorkerHostOptions
{
    public string? ConnectionString { get; set; }
    public string? ExistingTopic { get; set; }
    public string? ExistingDeadLetterTopic { get; set; }
    public string? ExistingGroupId { get; set; }
    public bool UseRealTimescaleProcessing { get; set; }
    public bool UseProductionDeadLetterPublisher { get; set; }
    public Action<KafkaOptions>? ConfigureKafka { get; set; }
    public Action<ControllableDeadLetterPublisher>? ConfigureDeadLetterPublisher { get; set; }
}

public sealed class ControllableTelemetryProcessingUnitOfWork : ITelemetryProcessingUnitOfWork
{
    private readonly Dictionary<Guid, byte> _processed = new();

    public Func<TelemetryEvent, CancellationToken, Task<ProcessTelemetryOutcome>>? Handler { get; set; }

    public int CallCount { get; private set; }

    public Task<ProcessTelemetryOutcome> ProcessAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (Handler is not null)
            return Handler(telemetryEvent, cancellationToken);

        if (_processed.ContainsKey(telemetryEvent.EventId))
            return Task.FromResult(ProcessTelemetryOutcome.Duplicate);

        _processed[telemetryEvent.EventId] = 1;
        return Task.FromResult(ProcessTelemetryOutcome.Processed);
    }
}

public sealed class ControllableDeadLetterPublisher : IDeadLetterPublisher, IDisposable
{
    private int _failuresBeforeSuccess;
    private int _failures;

    public List<DeadLetterMessage> Messages { get; } = [];
    public int PublishAttempts { get; private set; }

    public void FailUntilAttempt(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

    public Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
    {
        PublishAttempts++;

        if (_failures < _failuresBeforeSuccess)
        {
            _failures++;
            throw new FleetTelemetry.Application.Exceptions.DeadLetterPublishException("simulated dlq failure");
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
