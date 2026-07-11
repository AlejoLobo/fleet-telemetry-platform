using System.Text.Json;
using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Exceptions;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Kafka;
using FleetTelemetry.Infrastructure.Observability;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace FleetTelemetry.Integration.Tests;

[Collection(KafkaIntegrationCollection.Name)]
public class KafkaDeadLetterPublisherIntegrationTests
{
    private readonly KafkaIntegrationFixture _kafka;

    public KafkaDeadLetterPublisherIntegrationTests(KafkaIntegrationFixture fixture) => _kafka = fixture;

    [Fact]
    public async Task PublishAsync_serializes_camelCase_publishes_to_real_topic_and_uses_expected_key()
    {
        var dlqTopic = _kafka.NewTopicName("dlq-publisher-direct");
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            var message = CreateMessage(
                originalPayload: """{"vehicleId":"VH-001"}""",
                reason: "invalid_payload",
                originalTopic: "telemetry.raw",
                partition: 2,
                offset: 17);

            using var publisher = CreatePublisher(_kafka.BootstrapServers, dlqTopic);
            await publisher.PublishAsync(message);

            var consumed = await KafkaDlqTestHelper.WaitUntilDlqMessageAsync(
                _kafka.BootstrapServers,
                dlqTopic,
                TimeSpan.FromSeconds(20));

            Assert.Equal("telemetry.raw:2:17", consumed.Message.Key);

            var json = KafkaDlqTestHelper.ParseDlqPayload(consumed.Message.Value!);
            Assert.Equal(message.OriginalPayload, json.GetProperty("originalPayload").GetString());
            Assert.Equal(message.Reason, json.GetProperty("reason").GetString());
            Assert.Equal(message.ExceptionMessage, json.GetProperty("exceptionMessage").GetString());
            Assert.Equal(message.OriginalTopic, json.GetProperty("originalTopic").GetString());
            Assert.Equal(message.Partition, json.GetProperty("partition").GetInt32());
            Assert.Equal(message.Offset, json.GetProperty("offset").GetInt64());
            Assert.True(json.TryGetProperty("occurredAt", out _));
            Assert.False(json.TryGetProperty("OriginalPayload", out _));
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(dlqTopic);
        }
    }

    [Fact]
    public async Task PublishAsync_when_circuit_breaker_is_open_throws_DeadLetterPublishException()
    {
        var dlqTopic = _kafka.NewTopicName("dlq-publisher-cb");
        await _kafka.CreateTopicAsync(dlqTopic);

        try
        {
            using var publisher = CreatePublisher(
                bootstrapServers: "127.0.0.1:59999",
                deadLetterTopic: dlqTopic,
                configureResilience: options =>
                {
                    options.Kafka.Enabled = true;
                    options.Kafka.MinimumThroughput = 2;
                    options.Kafka.FailureRatio = 0.5;
                    options.Kafka.MaxRetryAttempts = 1;
                    options.Kafka.RetryDelayMilliseconds = 10;
                    options.Kafka.SamplingDurationSeconds = 60;
                    options.Kafka.BreakDurationSeconds = 60;
                },
                configureKafka: options => options.ProducerMessageTimeoutMs = 500);

            var message = CreateMessage("{}", "processing_failure", "topic", 0, 0);

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await publisher.PublishAsync(message);
                }
                catch (DeadLetterPublishException)
                {
                }
            }

            var exception = await Assert.ThrowsAsync<DeadLetterPublishException>(() => publisher.PublishAsync(message));
            Assert.IsType<BrokenCircuitException>(exception.InnerException);
        }
        finally
        {
            await _kafka.DeleteTrackedTopicsAsync(dlqTopic);
        }
    }

    private static DeadLetterMessage CreateMessage(
        string originalPayload,
        string reason,
        string originalTopic,
        int partition,
        long offset) =>
        new(
            DeadLetterId: Guid.NewGuid(),
            SchemaVersion: 1,
            Category: "test",
            ErrorCode: reason,
            AttemptNumber: 1,
            OccurredAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            OriginalTopic: originalTopic,
            Partition: partition,
            Offset: offset,
            MessageKey: null,
            CorrelationId: null,
            OriginalPayload: originalPayload,
            TechnicalDetail: "test failure",
            Reason: reason,
            ExceptionMessage: "test failure");

    private static KafkaDeadLetterPublisher CreatePublisher(
        string bootstrapServers,
        string deadLetterTopic,
        Action<ResilienceOptions>? configureResilience = null,
        Action<KafkaOptions>? configureKafka = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        services.Configure<KafkaOptions>(options =>
        {
            options.BootstrapServers = bootstrapServers;
            options.DeadLetterTopic = deadLetterTopic;
            configureKafka?.Invoke(options);
        });

        services.Configure<ResilienceOptions>(options =>
        {
            configureResilience?.Invoke(options);
        });

        services.AddSingleton<ICircuitBreakerStateRegistry, CircuitBreakerStateRegistry>();
        services.AddSingleton<ResiliencePipelineFactory>();
        services.AddSingleton(_ => new FleetTelemetryMetrics());
        services.AddSingleton<KafkaDeadLetterPublisher>();

        return services.BuildServiceProvider().GetRequiredService<KafkaDeadLetterPublisher>();
    }
}
