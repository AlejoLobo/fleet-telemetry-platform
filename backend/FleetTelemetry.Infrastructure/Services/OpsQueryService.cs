using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Services;

// Agrega métricas operativas reutilizando consultas de flota y alertas.
public class OpsQueryService : IOpsQueryService
{
    private readonly IFleetQueryService _fleetQueryService;
    private readonly IAlertRepository _alertRepository;
    private readonly KafkaOptions _kafkaOptions;

    public OpsQueryService(
        IFleetQueryService fleetQueryService,
        IAlertRepository alertRepository,
        IOptions<KafkaOptions> kafkaOptions)
    {
        _fleetQueryService = fleetQueryService;
        _alertRepository = alertRepository;
        _kafkaOptions = kafkaOptions.Value;
    }

    public async Task<OpsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetQueryService.GetLatestVehicleStatusesAsync(
            liveOnly: false,
            excludeSimulated: false,
            cancellationToken);
        var alerts = await _alertRepository.GetOpenAlertsAsync(cancellationToken);

        var activeVehicles = vehicles.Count(v => v.Status == "online");
        var criticalAlerts = alerts.Count(a =>
            string.Equals(a.Severity, "critical", StringComparison.OrdinalIgnoreCase));

        DateTimeOffset? lastTelemetry = null;
        foreach (var vehicle in vehicles)
        {
            if (vehicle.LastSeenAt is not { } seenAt)
                continue;
            if (lastTelemetry is null || seenAt > lastTelemetry)
                lastTelemetry = seenAt;
        }

        return new OpsSummaryResponse(
            TotalVehicles: vehicles.Count,
            ActiveVehicles: activeVehicles,
            CriticalAlerts: criticalAlerts,
            LastTelemetryAt: lastTelemetry,
            SseMode: "polling",
            TelemetryTopic: _kafkaOptions.TelemetryTopic,
            DeadLetterTopic: _kafkaOptions.DeadLetterTopic);
    }
}
