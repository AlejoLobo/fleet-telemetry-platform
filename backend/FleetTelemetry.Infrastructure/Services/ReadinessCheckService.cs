using Confluent.Kafka;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

// Readiness: TimescaleDB + metadata Kafka (sin publicar mensajes de negocio).
public class ReadinessCheckService : IReadinessCheckService
{
    private const string ServiceName = "fleet-telemetry-api";
    private static readonly TimeSpan KafkaMetadataTimeout = TimeSpan.FromSeconds(3);

    private readonly FleetDbContext _dbContext;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<ReadinessCheckService> _logger;

    public ReadinessCheckService(
        FleetDbContext dbContext,
        IOptions<KafkaOptions> kafkaOptions,
        ILogger<ReadinessCheckService> logger)
    {
        _dbContext = dbContext;
        _kafkaOptions = kafkaOptions.Value;
        _logger = logger;
    }

    public async Task<ReadinessCheckResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["timescaledb"] = await CheckTimescaleAsync(cancellationToken),
            ["kafka"] = CheckKafka()
        };

        var ready = checks.Values.All(status => status == "ok");
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
}
