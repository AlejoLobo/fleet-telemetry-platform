using FleetTelemetry.Application.Interfaces;
using System.Text;

namespace FleetTelemetry.Application.Services;

public class AiOperationalTools
{
    private const double StoppedSpeedThresholdKmh = 1;
    private const double DefaultHighSpeedKmh = 80;

    private readonly IFleetQueryService _fleetQueryService;
    private readonly IAlertRepository _alertRepository;
    private readonly IAnalyticsQueryService _analyticsQueryService;

    public AiOperationalTools(
        IFleetQueryService fleetQueryService,
        IAlertRepository alertRepository,
        IAnalyticsQueryService analyticsQueryService)
    {
        _fleetQueryService = fleetQueryService;
        _alertRepository = alertRepository;
        _analyticsQueryService = analyticsQueryService;
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetLatestVehicleStatusAsync(
        string vehicleId,
        CancellationToken cancellationToken = default)
    {
        var status = await _fleetQueryService.GetVehicleStatusAsync(vehicleId, cancellationToken);
        if (status is null)
            return ($"No hay telemetría registrada para el vehículo {vehicleId}.", ["GetLatestVehicleStatus"]);

        var answer = new StringBuilder()
            .AppendLine($"Estado de {status.VehicleId}:")
            .AppendLine($"- Estado: {AiResponseFormatter.EtiquetaEstado(status.Status)}")
            .AppendLine($"- Última señal: {status.LastSeenAt:u}")
            .AppendLine($"- Velocidad: {status.LastSpeedKmh:F1} km/h")
            .AppendLine($"- Ubicación: {status.LastLatitude:F4}, {status.LastLongitude:F4}")
            .ToString();

        return (answer.Trim(), ["GetLatestVehicleStatus", "IFleetQueryService"]);
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetStoppedVehiclesAsync(
        CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetQueryService.GetLatestVehicleStatusesAsync(cancellationToken: cancellationToken);
        var stopped = vehicles
            .Where(v => v.LastSpeedKmh is <= StoppedSpeedThresholdKmh)
            .ToList();

        if (stopped.Count == 0)
            return ("No hay vehículos detenidos según la última telemetría.", ["GetStoppedVehicles"]);

        var lines = stopped.Select(v => $"- {v.VehicleId} (última señal: {v.LastSeenAt:u})");
        return ($"Vehículos detenidos ({stopped.Count}):\n{string.Join('\n', lines)}", ["GetStoppedVehicles"]);
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetVehiclesWithCriticalAlertsAsync(
        CancellationToken cancellationToken = default)
    {
        var alerts = await _alertRepository.GetOpenAlertsAsync(cancellationToken);
        var critical = alerts.Where(a => a.Severity == "critical").ToList();

        if (critical.Count == 0)
            return ("No hay alertas críticas abiertas.", ["GetVehiclesWithCriticalAlerts"]);

        var lines = critical.Select(a =>
            $"- {a.VehicleId}: {AiResponseFormatter.EtiquetaTipoAlerta(a.AlertType)} — {AiResponseFormatter.TraducirMensajeAlerta(a.VehicleId, a.Message)}");
        return ($"Alertas críticas abiertas ({critical.Count}):\n{string.Join('\n', lines)}", ["GetVehiclesWithCriticalAlerts"]);
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetVehiclesAboveSpeedAsync(
        double thresholdKmh,
        CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetQueryService.GetLatestVehicleStatusesAsync(cancellationToken: cancellationToken);
        var above = vehicles.Where(v => v.LastSpeedKmh > thresholdKmh).ToList();

        if (above.Count == 0)
            return ($"Ningún vehículo supera {thresholdKmh:F0} km/h.", ["GetVehiclesAboveSpeed"]);

        var lines = above.Select(v => $"- {v.VehicleId}: {v.LastSpeedKmh:F1} km/h");
        return ($"Vehículos por encima de {thresholdKmh:F0} km/h ({above.Count}):\n{string.Join('\n', lines)}", ["GetVehiclesAboveSpeed"]);
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetAnalyticsSummaryAsync(
        string? vehicleId,
        CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetQueryService.GetLatestVehicleStatusesAsync(cancellationToken: cancellationToken);
        if (vehicles.Count == 0)
            return ("No hay datos de flota para generar analítica.", ["GetAnalyticsSummary"]);

        var targetId = vehicleId ?? vehicles[0].VehicleId;
        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-24);
        var avgSpeed = await _analyticsQueryService.GetAverageSpeedAsync(targetId, from, to, cancellationToken);

        var answer = new StringBuilder()
            .AppendLine($"Resumen analítico (últimas 24 h) — vehículo {targetId}:")
            .AppendLine($"- Velocidad promedio: {avgSpeed:F1} km/h")
            .AppendLine($"- Vehículos en línea: {vehicles.Count(v => v.Status == "online")}/{vehicles.Count}")
            .AppendLine("- Fuente: TimescaleDB")
            .ToString();

        return (answer.Trim(), ["GetAnalyticsSummary", "IAnalyticsQueryService"]);
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetFleetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetQueryService.GetLatestVehicleStatusesAsync(cancellationToken: cancellationToken);
        var alerts = await _alertRepository.GetOpenAlertsAsync(cancellationToken);

        var answer = new StringBuilder()
            .AppendLine("Resumen operativo de flota:")
            .AppendLine($"- Vehículos con telemetría: {vehicles.Count}")
            .AppendLine($"- En línea: {vehicles.Count(v => v.Status == "online")}")
            .AppendLine($"- Desconectados: {vehicles.Count(v => v.Status == "offline")}")
            .AppendLine($"- Alertas abiertas: {alerts.Count}")
            .ToString();

        return (answer.Trim(), ["GetFleetOverview"]);
    }

    public static double ParseSpeedThreshold(string question, double defaultKmh = DefaultHighSpeedKmh)
    {
        var digits = new string(question.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, out var value) && value > 0 ? value : defaultKmh;
    }

    public static string? ExtractVehicleId(string question)
    {
        const string prefix = "VH-";
        var upper = question.ToUpperInvariant();
        var index = upper.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var end = index + prefix.Length;
        while (end < upper.Length && (char.IsDigit(upper[end]) || upper[end] == '-'))
            end++;

        return upper[index..end];
    }
}
