using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Application.Tests.TestHelpers;
using FleetTelemetry.Application.Interfaces;
using FleetTelemetry.Application.Services;
using FleetTelemetry.Domain.Entities;

// Pruebas de herramientas operativas del agente IA.
namespace FleetTelemetry.Application.Tests;

public class AiOperationalToolsTests
{
    private static readonly Guid DeviceA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DeviceB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111 está detenido", "11111111-1111-1111-1111-111111111111")]
    [InlineData("estado de 22222222-2222-2222-2222-222222222222", "22222222-2222-2222-2222-222222222222")]
    public void ExtractDeviceId_parses_id(string question, string expected)
    {
        var id = AiOperationalTools.ExtractDeviceId(question);
        Assert.Equal(Guid.Parse(expected), id);
    }

    [Fact]
    public void ParseSpeedThreshold_reads_number_from_question()
    {
        var threshold = AiOperationalTools.ParseSpeedThreshold("vehículos por encima de 95 km/h");
        Assert.Equal(95, threshold);
    }

    [Fact]
    public async Task GetVehiclesStoppedLongerThanAsync_filters_critical_zones()
    {
        var operational = new FakeOperationalQueryService();
        var tools = new AiOperationalTools(
            new TestHelpers.FakeFleetQueryService([]),
            operational,
            new FakeAlertRepository(),
            new FakeAnalyticsQueryService());

        var (answer, sources) = await tools.GetVehiclesStoppedLongerThanAsync(
            20,
            criticalZonesOnly: true,
            zoneName: null);

        Assert.Contains(DeviceA.ToString("D"), answer);
        Assert.DoesNotContain(DeviceB.ToString("D"), answer);
        Assert.Contains("CriticalZoneCatalog", sources);
    }

    private sealed class FakeOperationalQueryService : IFleetOperationalQueryService
    {
        public Task<IReadOnlyList<StoppedVehicleStatusDto>> GetVehiclesStoppedLongerThanAsync(
            TimeSpan minDuration,
            double stoppedSpeedThresholdKmh = 1,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoppedVehicleStatusDto>>(
            [
                new(DeviceA, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-25),
                    TimeSpan.FromMinutes(25), 4.598, -74.075, "Centro"),
                new(DeviceB, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-30),
                    TimeSpan.FromMinutes(30), 4.711, -74.032, null),
            ]);
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
            Task.FromResult(0d);
    }
}
