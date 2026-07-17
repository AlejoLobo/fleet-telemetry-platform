using System.Text.Json;
using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Tests.TestHelpers;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetTelemetry.Application.Tests;

public class AiToolRouterTests
{
    [Fact]
    public async Task RouteAsync_rejects_unsupported_query_intent()
    {
        var router = CreateRouter();

        var result = await router.RouteAsync(AiQuestionIntent.Unsupported());

        Assert.False(result.Success);
        Assert.Null(result.ToolName);
        Assert.Contains("unsupported_query", result.Sources);
        Assert.Contains("operativas", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RouteAsync_routes_fleet_overview()
    {
        var router = CreateRouter();

        var result = await router.RouteAsync(AiQuestionIntent.FleetOverview());

        Assert.True(result.Success);
        Assert.Equal(AiToolCatalog.GetFleetOverview, result.ToolName);
        Assert.Contains("Resumen operativo", result.Answer);
    }

    [Fact]
    public async Task RouteAsync_rejects_vehicle_status_without_id()
    {
        var router = CreateRouter();
        var intent = new AiQuestionIntent(AiQueryIntent.VehicleStatus, null, null, null, null, false);

        var result = await router.RouteAsync(intent);

        Assert.False(result.Success);
        Assert.Equal(AiToolCatalog.GetLatestVehicleStatus, result.ToolName);
        Assert.Contains("invalid_parameters", result.Sources);
    }

    [Fact]
    public void ReduceResultLines_truncates_long_lists()
    {
        var answer = "Vehículos detenidos (5):\n" +
                     "- VH-001\n" +
                     "- VH-002\n" +
                     "- VH-003\n" +
                     "- VH-004\n" +
                     "- VH-005";

        var reduced = AiToolRouter.ReduceResultLines(answer, 3);

        Assert.Contains("VH-001", reduced);
        Assert.Contains("VH-002", reduced);
        Assert.DoesNotContain("VH-005", reduced);
        Assert.Contains("resultado reducido", reduced, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteToolCallAsync_rejects_unknown_tool()
    {
        var router = CreateRouter();
        var result = await router.ExecuteToolCallAsync(
            "GetWeatherForecast",
            new Dictionary<string, JsonElement>());

        Assert.False(result.Success);
        Assert.Contains("unsupported_tool", result.Sources);
    }

    private static AiToolRouter CreateRouter()
    {
        var tools = new AiOperationalTools(
            new TestHelpers.FakeFleetQueryService(
            [
                new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Vehículo 1", "car", "online", DateTimeOffset.UtcNow, 0, 4.65, -74.08, 90)
            ]),
            new FakeOperationalQueryService(),
            new FakeAlertRepository(),
            new FakeAnalyticsQueryService());

        return new AiToolRouter(tools, NullLogger<AiToolRouter>.Instance);
    }

    private sealed class FakeOperationalQueryService : IFleetOperationalQueryService
    {
        public Task<IReadOnlyList<StoppedVehicleStatusDto>> GetVehiclesStoppedLongerThanAsync(
            TimeSpan minDuration,
            double stoppedSpeedThresholdKmh = 1,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoppedVehicleStatusDto>>([]);
    }

    private sealed class FakeAlertRepository : IAlertRepository
    {
        public Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FleetAlert>>([]);

        public Task<IReadOnlyList<FleetAlert>> GetOpenAlertsAfterCursorAsync(
            AlertStreamCursor cursor,
            DateTimeOffset upperBound,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FleetAlert>>([]);

        public Task<bool> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task SaveAsync(FleetAlert alert, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAnalyticsQueryService : IAnalyticsQueryService
    {
        public Task<double> GetAverageSpeedAsync(
            Guid deviceId,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(42d);
    }
}
