using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Resilience;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Observability;
using FleetTelemetry.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Worker.Tests;

public class TelemetryMessageCoordinatorTests
{
    private const string Topic = "telemetry.raw";

    [Fact]
    public async Task Permanent_error_runs_process_once_even_when_dlq_fails_multiple_times()
    {
        var dlq = new FlakyDeadLetterPublisher(failuresBeforeSuccess: 3);
        var processCalls = 0;
        var coordinator = CreateCoordinator(dlq, maxDlqAttempts: 5, retryInitialMs: 1, retryMaxMs: 5);

        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 10);

        var result = await coordinator.ProcessUntilTerminalAsync(
            message,
            async (_, _) =>
            {
                Interlocked.Increment(ref processCalls);
                throw new InvalidOperationException("permanent");
            },
            CancellationToken.None);

        Assert.Equal(CoordinatorResult.Commit, result);
        Assert.Equal(1, processCalls);
        Assert.Equal(4, dlq.PublishAttempts);
        Assert.Single(dlq.Messages);
    }

    [Fact]
    public async Task Processing_attempts_do_not_reset_when_dlq_publish_fails()
    {
        var dlq = new FlakyDeadLetterPublisher(failuresBeforeSuccess: 2);
        var attempts = new List<int>();
        var coordinator = CreateCoordinator(dlq, maxAttempts: 3, maxDlqAttempts: 5, retryInitialMs: 1, retryMaxMs: 5);

        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 11);

        var result = await coordinator.ProcessUntilTerminalAsync(
            message,
            async (_, _) =>
            {
                attempts.Add(attempts.Count + 1);
                throw new TimeoutException("transient");
            },
            CancellationToken.None);

        Assert.Equal(CoordinatorResult.Commit, result);
        Assert.Equal([1, 2, 3], attempts);
        Assert.Equal(3, dlq.PublishAttempts);
    }

    [Fact]
    public async Task Dlq_failure_then_recovery_commits_once()
    {
        var dlq = new FlakyDeadLetterPublisher(failuresBeforeSuccess: 2);
        var coordinator = CreateCoordinator(dlq, maxDlqAttempts: 5, retryInitialMs: 1, retryMaxMs: 5);
        var message = new KafkaConsumedMessage("{ bad", Topic, 0, 12);

        var result = await coordinator.ProcessUntilTerminalAsync(
            message,
            (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed),
            CancellationToken.None);

        Assert.Equal(CoordinatorResult.Commit, result);
        Assert.Equal(3, dlq.PublishAttempts);
        Assert.Single(dlq.Messages);
    }

    [Fact]
    public async Task Dlq_exceeding_limit_stops_without_commit()
    {
        var lifetime = new TestHostLifetime();
        var dlq = new FlakyDeadLetterPublisher(failuresBeforeSuccess: int.MaxValue);
        var coordinator = CreateCoordinator(
            dlq,
            lifetime: lifetime,
            maxDlqAttempts: 3,
            retryInitialMs: 1,
            retryMaxMs: 5);
        var message = new KafkaConsumedMessage("{ bad", Topic, 0, 13);

        var result = await coordinator.ProcessUntilTerminalAsync(
            message,
            (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed),
            CancellationToken.None);

        Assert.Equal(CoordinatorResult.StopWithoutCommit, result);
        Assert.True(lifetime.StopRequested);
        Assert.Equal(3, dlq.PublishAttempts);
    }

    [Fact]
    public async Task Cancellation_during_dlq_backoff_ends_cleanly()
    {
        var dlq = new FlakyDeadLetterPublisher(failuresBeforeSuccess: int.MaxValue);
        var coordinator = CreateCoordinator(dlq, maxDlqAttempts: 10, retryInitialMs: 5_000, retryMaxMs: 5_000);
        var message = new KafkaConsumedMessage("{ bad", Topic, 0, 14);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await coordinator.ProcessUntilTerminalAsync(
            message,
            (_, _) => Task.FromResult(ProcessTelemetryOutcome.Processed),
            cts.Token);

        Assert.Equal(CoordinatorResult.CancelledWithoutCommit, result);
    }

    [Fact]
    public async Task Cancellation_during_processing_backoff_ends_cleanly()
    {
        var dlq = new RecordingDeadLetterPublisher();
        var coordinator = CreateCoordinator(dlq, maxAttempts: 5, retryInitialMs: 5_000, retryMaxMs: 5_000);
        var payload = TelemetryEventJsonSerializer.Serialize(CreateValidEvent());
        var message = new KafkaConsumedMessage(payload, Topic, 0, 15);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await coordinator.ProcessUntilTerminalAsync(
            message,
            async (_, _) => throw new TimeoutException("transient"),
            cts.Token);

        Assert.Equal(CoordinatorResult.CancelledWithoutCommit, result);
    }

    private static TelemetryMessageCoordinator CreateCoordinator(
        IDeadLetterPublisher dlq,
        IHostApplicationLifetime? lifetime = null,
        int maxAttempts = 3,
        int maxDlqAttempts = 5,
        int retryInitialMs = 500,
        int retryMaxMs = 5000)
    {
        var options = Options.Create(new KafkaOptions
        {
            TelemetryTopic = Topic,
            DeadLetterTopic = "telemetry.dead-letter",
            MaxProcessingAttempts = maxAttempts,
            MaxDeadLetterPublishAttempts = maxDlqAttempts,
            RetryInitialDelayMilliseconds = retryInitialMs,
            RetryMaxDelayMilliseconds = retryMaxMs
        });

        var processor = new TelemetryMessageProcessor(dlq, options, new FleetTelemetryMetrics(), NullLogger<TelemetryMessageProcessor>.Instance);

        return new TelemetryMessageCoordinator(
            new NoOpScopeFactory(),
            options,
            CreateResilience(),
            processor,
            lifetime ?? new TestHostLifetime(),
            NullLogger<TelemetryMessageCoordinator>.Instance);
    }

    private static ResiliencePipelineFactory CreateResilience() =>
        new(
            Options.Create(new ResilienceOptions()),
            new CircuitBreakerStateRegistry(),
            NullLogger<ResiliencePipelineFactory>.Instance);

    private static TelemetryEvent CreateValidEvent() => new()
    {
        EventId = Guid.NewGuid(),
        VehicleId = "VH-COORD",
        DriverId = "DRV-001",
        Timestamp = DateTimeOffset.UtcNow,
        Latitude = 4.65,
        Longitude = -74.08,
        SpeedKmh = 40,
        FuelLevelPercent = 70,
        BatteryPercent = 80
    };

    private sealed class FlakyDeadLetterPublisher : IDeadLetterPublisher
    {
        private readonly int _failuresBeforeSuccess;
        private int _failures;

        public FlakyDeadLetterPublisher(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

        public List<DeadLetterMessage> Messages { get; } = [];
        public int PublishAttempts { get; private set; }

        public Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            PublishAttempts++;
            if (_failures < _failuresBeforeSuccess)
            {
                _failures++;
                throw new DeadLetterPublishException("simulated dlq failure");
            }

            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDeadLetterPublisher : IDeadLetterPublisher
    {
        public List<DeadLetterMessage> Messages { get; } = [];

        public Task PublishAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostLifetime : IHostApplicationLifetime
    {
        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopRequested = true;
    }

    private sealed class NoOpScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new NoOpScope();

        private sealed class NoOpScope : IServiceScope
        {
            public IServiceProvider ServiceProvider => throw new NotSupportedException();
            public void Dispose() { }
        }
    }
}
