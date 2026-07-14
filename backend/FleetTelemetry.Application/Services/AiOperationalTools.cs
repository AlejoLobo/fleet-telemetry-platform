using FleetTelemetry.Application.Interfaces;
using System.Text;

namespace FleetTelemetry.Application.Services;

public class AiOperationalTools
{
    private const double StoppedSpeedThresholdKmh = 1;
    private const double DefaultHighSpeedKmh = 80;

    private readonly IFleetQueryService _fleetQueryService;
    private readonly IFleetOperationalQueryService _operationalQueryService;
    private readonly IAlertRepository _alertRepository;
    private readonly IAnalyticsQueryService _analyticsQueryService;

    public AiOperationalTools(
        IFleetQueryService fleetQueryService,
        IFleetOperationalQueryService operationalQueryService,
        IAlertRepository alertRepository,
        IAnalyticsQueryService analyticsQueryService)
    {
        _fleetQueryService = fleetQueryService;
        _operationalQueryService = operationalQueryService;
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

        var zone = status.LastLatitude is double lat && status.LastLongitude is double lng
            ? CriticalZoneCatalog.FindZoneAt(lat, lng)
            : null;

        var answer = new StringBuilder()
            .AppendLine($"Estado de {status.VehicleId}:")
            .AppendLine($"- Estado: {AiResponseFormatter.EtiquetaEstado(status.Status)}")
            .AppendLine($"- Última señal: {status.LastSeenAt:u}")
            .AppendLine($"- Velocidad: {status.LastSpeedKmh:F1} km/h")
            .AppendLine($"- Ubicación: {status.LastLatitude:F4}, {status.LastLongitude:F4}");

        if (zone is not null)
            answer.AppendLine($"- Zona operativa: {zone.Name} (crítica)");

        return (answer.ToString().Trim(), ["GetLatestVehicleStatus", "IFleetQueryService"]);
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetStoppedVehiclesAsync(
        CancellationToken cancellationToken = default)
    {
        var vehicles = await _fleetQueryService.GetAllFleetStatusesAsync(cancellationToken: cancellationToken);
        var stopped = vehicles
            .Where(v => v.LastSpeedKmh is <= StoppedSpeedThresholdKmh)
            .ToList();

        if (stopped.Count == 0)
            return ("No hay vehículos detenidos según la última telemetría.", ["GetStoppedVehicles"]);

        var lines = stopped.Select(FormatStoppedLine);
        return ($"Vehículos detenidos en este instante ({stopped.Count}):\n{string.Join('\n', lines)}",
            ["GetStoppedVehicles"]);
    }

    public async Task<(string Answer, IReadOnlyList<string> Sources)> GetVehiclesStoppedLongerThanAsync(
        int minutes,
        bool criticalZonesOnly = false,
        string? zoneName = null,
        CancellationToken cancellationToken = default)
    {
        var minDuration = TimeSpan.FromMinutes(Math.Max(1, minutes));
        var stopped = await _operationalQueryService.GetVehiclesStoppedLongerThanAsync(
            minDuration,
            StoppedSpeedThresholdKmh,
            cancellationToken);

        if (criticalZonesOnly || zoneName is not null)
        {
            stopped = stopped
                .Where(v =>
                {
                    if (zoneName is not null)
                    {
                        var zone = CriticalZoneCatalog.FindZoneAt(v.Latitude, v.Longitude);
                        return zone is not null && zone.Name.Equals(zoneName, StringComparison.OrdinalIgnoreCase);
                    }

                    return v.CriticalZoneName is not null;
                })
                .ToList();
        }

        if (stopped.Count == 0)
        {
            var scope = DescribeScope(minutes, criticalZonesOnly, zoneName);
            return ($"No hay vehículos detenidos {scope}.", ["GetVehiclesStoppedLongerThan", "IFleetOperationalQueryService"]);
        }

        var scopeLabel = DescribeScope(minutes, criticalZonesOnly, zoneName);
        var lines = stopped.Select(v =>
        {
            var zoneLabel = v.CriticalZoneName is not null ? $", zona {v.CriticalZoneName}" : string.Empty;
            return $"- {v.VehicleId}: detenido {FormatDuration(v.StoppedDuration)} (desde {v.StoppedSince:u}{zoneLabel})";
        });

        var sources = new List<string> { "GetVehiclesStoppedLongerThan", "IFleetOperationalQueryService" };
        if (criticalZonesOnly || zoneName is not null)
            sources.Add("CriticalZoneCatalog");

        return ($"Vehículos detenidos {scopeLabel} ({stopped.Count}):\n{string.Join('\n', lines)}", sources);
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
        var vehicles = await _fleetQueryService.GetAllFleetStatusesAsync(cancellationToken: cancellationToken);
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
        var vehicles = await _fleetQueryService.GetAllFleetStatusesAsync(cancellationToken: cancellationToken);
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
        var vehicles = await _fleetQueryService.GetAllFleetStatusesAsync(cancellationToken: cancellationToken);
        var alerts = await _alertRepository.GetOpenAlertsAsync(cancellationToken);
        var stoppedLong = await _operationalQueryService.GetVehiclesStoppedLongerThanAsync(
            TimeSpan.FromMinutes(20),
            StoppedSpeedThresholdKmh,
            cancellationToken);
        var stoppedInCritical = stoppedLong.Count(v => v.CriticalZoneName is not null);

        var answer = new StringBuilder()
            .AppendLine("Resumen operativo de flota:")
            .AppendLine($"- Vehículos con telemetría: {vehicles.Count}")
            .AppendLine($"- En línea: {vehicles.Count(v => v.Status == "online")}")
            .AppendLine($"- Desconectados: {vehicles.Count(v => v.Status == "offline")}")
            .AppendLine($"- Alertas abiertas: {alerts.Count}")
            .AppendLine($"- Detenidos ≥ 20 min: {stoppedLong.Count}")
            .AppendLine($"- Detenidos ≥ 20 min en zonas críticas: {stoppedInCritical}")
            .ToString();

        return (answer.Trim(), ["GetFleetOverview", "IFleetOperationalQueryService"]);
    }

    public static double ParseSpeedThreshold(string question, double defaultKmh = DefaultHighSpeedKmh) =>
        ParseSpeedThresholdOrNull(question) ?? defaultKmh;

    public static double? ParseSpeedThresholdOrNull(string question)
    {
        var digits = new string(question.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, out var value) && value > 0 ? value : null;
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

    private static string FormatStoppedLine(DTOs.VehicleLatestStatusResponse vehicle)
    {
        var zone = vehicle.LastLatitude is double lat && vehicle.LastLongitude is double lng
            ? CriticalZoneCatalog.FindZoneAt(lat, lng)?.Name
            : null;
        var zoneSuffix = zone is not null ? $", zona {zone}" : string.Empty;
        return $"- {vehicle.VehicleId} (última señal: {vehicle.LastSeenAt:u}{zoneSuffix})";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{duration.TotalHours:F1} h";
        return $"{(int)Math.Round(duration.TotalMinutes)} min";
    }

    private static string DescribeScope(int minutes, bool criticalZonesOnly, string? zoneName)
    {
        if (zoneName is not null)
            return $"≥ {minutes} min en zona {zoneName}";
        if (criticalZonesOnly)
            return $"≥ {minutes} min en zonas críticas ({string.Join(", ", CriticalZoneCatalog.All.Select(z => z.Name))})";
        return $"≥ {minutes} min";
    }
}
