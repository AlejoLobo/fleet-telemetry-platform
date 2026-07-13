using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using FleetTelemetry.Infrastructure.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

// Readiness: TimescaleDB + metadata Kafka + consumidor KafkaPush de la réplica.
public class ReadinessCheckService : IReadinessCheckService
{
    private const string ServiceName = "fleet-telemetry-api";
    private static readonly TimeSpan KafkaMetadataTimeout = TimeSpan.FromSeconds(3);

    private readonly FleetDbContext _dbContext;
    private readonly KafkaOptions _kafkaOptions;
    private readonly SseOptions _sseOptions;
    private readonly IFleetKafkaPushReadiness _kafkaPushReadiness;
    private readonly ILogger<ReadinessCheckService> _logger;

    public ReadinessCheckService(
        FleetDbContext dbContext,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions,
        IFleetKafkaPushReadiness kafkaPushReadiness,
        ILogger<ReadinessCheckService> logger)
    {
        _dbContext = dbContext;
        _kafkaOptions = kafkaOptions.Value;
        _sseOptions = sseOptions.Value;
        _kafkaPushReadiness = kafkaPushReadiness;
        _logger = logger;
    }

    public async Task<ReadinessCheckResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["timescaledb"] = await CheckTimescaleAsync(cancellationToken),
            ["kafka"] = CheckKafka(),
            ["kafka_push"] = CheckKafkaPush()
        };

        var ready = checks.Values.All(status => status is "ok" or "bypassed");
        return new ReadinessCheckResponse(
            Status: ready ? "ready" : "not_ready",
            Service: ServiceName,
            Timestamp: DateTimeOffset.UtcNow,
            Checks: checks);
    }

    private async Task<string> CheckTimescaleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect ? "ok" : "unavailable";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness TimescaleDB check failed");
            return "unavailable";
        }
    }

    private string CheckKafka()
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _kafkaOptions.BootstrapServers,
                SocketTimeoutMs = (int)KafkaMetadataTimeout.TotalMilliseconds
            }).Build();

            var metadata = admin.GetMetadata(KafkaMetadataTimeout);
            return metadata.Brokers.Count > 0 ? "ok" : "unavailable";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness Kafka check failed");
            return "unavailable";
        }
    }

    private string CheckKafkaPush()
    {
        if (_sseOptions.Mode != SseDeliveryMode.KafkaPush)
            return "bypassed";

        return _kafkaPushReadiness.State switch
        {
            FleetKafkaPushReadinessState.Ready => "ok",
            FleetKafkaPushReadinessState.Faulted => "faulted",
            FleetKafkaPushReadinessState.Rebalancing => "rebalancing",
            _ => "starting"
        };
    }
}
