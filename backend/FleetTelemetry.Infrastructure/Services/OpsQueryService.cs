using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

public class OpsQueryService : IOpsQueryService
{
    private readonly IFleetStateAggregateRepository _aggregateRepository;
    private readonly KafkaOptions _kafkaOptions;
    private readonly SseOptions _sseOptions;

    public OpsQueryService(
        IFleetStateAggregateRepository aggregateRepository,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<SseOptions> sseOptions)
    {
        _aggregateRepository = aggregateRepository;
        _kafkaOptions = kafkaOptions.Value;
        _sseOptions = sseOptions.Value;
    }

    public async Task<OpsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _aggregateRepository.GetFleetAggregateSnapshotAsync(cancellationToken);
        var criticalAlerts = await _aggregateRepository.CountOpenCriticalAlertsAsync(cancellationToken);

        return new OpsSummaryResponse(
            TotalVehicles: snapshot.TotalVehicles,
            ActiveVehicles: snapshot.ActiveVehicles,
            CriticalAlerts: criticalAlerts,
            LastTelemetryAt: snapshot.LastTelemetryAt,
            SseMode: MapSseMode(_sseOptions.Mode),
            TelemetryTopic: _kafkaOptions.TelemetryTopic,
            DeadLetterTopic: _kafkaOptions.DeadLetterTopic);
    }

    private static string MapSseMode(SseDeliveryMode mode) =>
        mode == SseDeliveryMode.KafkaPush ? "kafka-push" : "polling";
}
