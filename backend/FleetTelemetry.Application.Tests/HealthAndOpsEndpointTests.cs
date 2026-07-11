using FleetTelemetry.Api.Controllers;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Domain.Entities;
using FleetTelemetry.Infrastructure.Configuration;
using FleetTelemetry.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Application.Tests;

public class HealthAndOpsEndpointTests
{
    [Fact]
    public void Health_live_returns_200_alive_without_dependencies()
    {
        var controller = new HealthController();

        var result = controller.Live();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("alive", json);
        Assert.Contains("fleet-telemetry-api", json);
        Assert.Contains("timestamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_ready_returns_200_when_dependencies_ok()
    {
        var readiness = new FakeReadinessCheckService(ready: true);
        var controller = new HealthController();

        var result = await controller.Ready(readiness, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var payload = Assert.IsType<ReadinessCheckResponse>(ok.Value);
        Assert.Equal("ready", payload.Status);
        Assert.Equal("ok", payload.Checks["timescaledb"]);
    }

    [Fact]
    public async Task Health_ready_returns_503_when_dependency_fails()
    {
        var readiness = new FakeReadinessCheckService(ready: false);
        var controller = new HealthController();

        var result = await controller.Ready(readiness, CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        var payload = Assert.IsType<ReadinessCheckResponse>(unavailable.Value);
        Assert.Equal("not_ready", payload.Status);
    }

    [Fact]
    public async Task Ops_summary_returns_expected_fields()
    {
        var fleet = new FakeFleetQueryService([
            new VehicleLatestStatusResponse("VH-001", "VH-001", "online", DateTimeOffset.UtcNow, 40, 4.6, -74.0, null),
            new VehicleLatestStatusResponse("VH-002", "VH-002", "offline", DateTimeOffset.UtcNow.AddHours(-1), 0, 4.7, -74.1, null)
        ]);
        var alerts = new FakeAlertRepository([
            new FleetAlert { AlertId = Guid.NewGuid(), VehicleId = "VH-001", Severity = "critical", AlertType = "overspeed", Message = "x", CreatedAt = DateTimeOffset.UtcNow },
            new FleetAlert { AlertId = Guid.NewGuid(), VehicleId = "VH-002", Severity = "warning", AlertType = "low_fuel", Message = "y", CreatedAt = DateTimeOffset.UtcNow }
        ]);
        var kafka = Options.Create(new KafkaOptions
        {
            TelemetryTopic = "telemetry.raw",
            DeadLetterTopic = "telemetry.dead-letter"
        });
        var service = new OpsQueryService(fleet, alerts, kafka);
        var controller = new OpsController(service);

        var result = await controller.Summary(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<OpsSummaryResponse>(ok.Value);
        Assert.Equal(2, summary.TotalVehicles);
        Assert.Equal(1, summary.ActiveVehicles);
        Assert.Equal(1, summary.CriticalAlerts);
        Assert.NotNull(summary.LastTelemetryAt);
        Assert.Equal("polling", summary.SseMode);
        Assert.Equal("telemetry.raw", summary.TelemetryTopic);
        Assert.Equal("telemetry.dead-letter", summary.DeadLetterTopic);
    }

    private sealed class FakeReadinessCheckService(bool ready) : IReadinessCheckService
    {
        public Task<ReadinessCheckResponse> CheckAsync(CancellationToken cancellationToken = default)
        {
            var checks = new Dictionary<string, string>
            {
                ["timescaledb"] = ready ? "ok" : "unavailable",
                ["kafka"] = ready ? "ok" : "unavailable"
            };
            return Task.FromResult(new ReadinessCheckResponse(
                Status: ready ? "ready" : "not_ready",
                Service: "fleet-telemetry-api",
                Timestamp: DateTimeOffset.UtcNow,
                Checks: checks));
        }
    }

    private sealed class FakeFleetQueryService(IReadOnlyList<VehicleLatestStatusResponse> vehicles) : IFleetQueryService
    {
        public Task<IReadOnlyList<VehicleLatestStatusResponse>> GetLatestVehicleStatusesAsync(
            bool liveOnly = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(vehicles);

        public Task<VehicleLatestStatusResponse?> GetVehicleStatusAsync(
            string vehicleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(vehicles.FirstOrDefault(v => v.VehicleId == vehicleId));
    }

    private sealed class FakeAlertRepository(IReadOnlyList<FleetAlert> alerts) : IAlertRepository
    {
        public Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(alerts);

        public Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAfterCursorAsync(
            AlertStreamCursor cursor,
            DateTimeOffset upperBound,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FleetAlert>>(alerts
                .Where(a => !a.IsAcknowledged && a.CreatedAt <= upperBound)
                .Where(a => a.CreatedAt > cursor.CreatedAt
                    || (a.CreatedAt == cursor.CreatedAt && a.AlertId.CompareTo(cursor.AlertId) > 0))
                .OrderBy(a => a.CreatedAt)
                .ThenBy(a => a.AlertId)
                .Take(limit)
                .ToList());

        public Task<bool> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
