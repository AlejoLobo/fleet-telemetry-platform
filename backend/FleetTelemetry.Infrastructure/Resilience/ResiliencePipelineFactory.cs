using Confluent.Kafka;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

// Fábrica de pipelines de resiliencia Polly.
namespace FleetTelemetry.Infrastructure.Resilience;

/// <summary>
/// Factory central de pipelines Polly. Circuit breaker externo + retry interno (fail-fast).
/// </summary>
// Construye pipelines para Kafka, OpenAI y base de datos.
public sealed class ResiliencePipelineFactory
{
    private readonly ICircuitBreakerStateRegistry _registry;
    private readonly ILogger<ResiliencePipelineFactory> _logger;

    public ResiliencePipeline KafkaPublishPipeline { get; }

    public ResiliencePipeline<HttpResponseMessage> OpenAiHttpPipeline { get; }

    public ResiliencePipeline DatabaseProcessingPipeline { get; }

    public ResiliencePipelineFactory(
        IOptions<ResilienceOptions> options,
        ICircuitBreakerStateRegistry registry,
        ILogger<ResiliencePipelineFactory> logger)
    {
        _registry = registry;
        _logger = logger;

        var config = options.Value;
        KafkaPublishPipeline = BuildKafkaPipeline(config.Kafka);
        OpenAiHttpPipeline = BuildOpenAiPipeline(config.OpenAi);
        DatabaseProcessingPipeline = BuildDatabasePipeline(config.TimescaleDb);
    }

    private ResiliencePipeline BuildKafkaPipeline(CircuitBreakerPolicyOptions options)
    {
        var shouldHandle = new PredicateBuilder()
            .Handle<KafkaException>()
            .Handle<ProduceException<string, string>>();

        var builder = new ResiliencePipelineBuilder();

        if (options.Enabled)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.FailureRatio,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.BreakDurationSeconds),
                ShouldHandle = shouldHandle,
                OnOpened = args => HandleOpened(ResilienceDependency.Kafka, args.BreakDuration, args.Outcome.Exception),
                OnClosed = _ => HandleClosed(ResilienceDependency.Kafka),
                OnHalfOpened = _ => HandleHalfOpened(ResilienceDependency.Kafka)
            });
        }

        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(options.RetryDelayMilliseconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = shouldHandle
        });

        return builder.Build();
    }

    private ResiliencePipeline<HttpResponseMessage> BuildOpenAiPipeline(CircuitBreakerPolicyOptions options)
    {
        var shouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
            .HandleResult(r => (int)r.StatusCode >= 500);

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        if (options.Enabled)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = options.FailureRatio,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.BreakDurationSeconds),
                ShouldHandle = shouldHandle,
                OnOpened = args => HandleOpened(ResilienceDependency.OpenAi, args.BreakDuration, args.Outcome.Exception),
                OnClosed = _ => HandleClosed(ResilienceDependency.OpenAi),
                OnHalfOpened = _ => HandleHalfOpened(ResilienceDependency.OpenAi)
            });
        }

        builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(options.RetryDelayMilliseconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = shouldHandle
        });

        return builder.Build();
    }

    private ResiliencePipeline BuildDatabasePipeline(CircuitBreakerPolicyOptions options)
    {
        var shouldHandle = new PredicateBuilder()
            .Handle<NpgsqlException>(IsTransientDatabaseError)
            .Handle<DbUpdateException>()
            .Handle<TimeoutException>();

        var builder = new ResiliencePipelineBuilder();

        if (options.Enabled)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.FailureRatio,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.BreakDurationSeconds),
                ShouldHandle = shouldHandle,
                OnOpened = args => HandleOpened(ResilienceDependency.TimescaleDb, args.BreakDuration, args.Outcome.Exception),
                OnClosed = _ => HandleClosed(ResilienceDependency.TimescaleDb),
                OnHalfOpened = _ => HandleHalfOpened(ResilienceDependency.TimescaleDb)
            });
        }

        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(options.RetryDelayMilliseconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = shouldHandle
        });

        return builder.Build();
    }

    private ValueTask HandleOpened(ResilienceDependency dependency, TimeSpan breakDuration, Exception? exception)
    {
        _registry.RecordTransition(dependency, CircuitBreakerState.Open);
        _logger.LogWarning(
            "Circuit breaker ABIERTO para {Dependency}. Pausa {BreakSeconds}s. Causa: {Reason}",
            dependency,
            breakDuration.TotalSeconds,
            exception?.Message ?? "ratio de fallos superado");
        return ValueTask.CompletedTask;
    }

    private ValueTask HandleClosed(ResilienceDependency dependency)
    {
        _registry.RecordTransition(dependency, CircuitBreakerState.Closed);
        _logger.LogInformation("Circuit breaker CERRADO para {Dependency}", dependency);
        return ValueTask.CompletedTask;
    }

    private ValueTask HandleHalfOpened(ResilienceDependency dependency)
    {
        _registry.RecordTransition(dependency, CircuitBreakerState.HalfOpen);
        _logger.LogInformation("Circuit breaker HALF-OPEN para {Dependency} (probando recuperación)", dependency);
        return ValueTask.CompletedTask;
    }

    private static bool IsTransientDatabaseError(NpgsqlException ex) =>
        ex.IsTransient || ex.SqlState is "40001" or "40P01" or "53300" or "57P03";
}
