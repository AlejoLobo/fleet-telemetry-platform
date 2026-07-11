using FleetTelemetry.Application.Services;

namespace FleetTelemetry.Application.Tests;

public class AiToolCatalogTests
{
    [Theory]
    [InlineData(AiToolCatalog.GetStoppedVehicles)]
    [InlineData(AiToolCatalog.GetVehiclesStoppedLongerThan)]
    [InlineData(AiToolCatalog.GetVehiclesWithCriticalAlerts)]
    [InlineData(AiToolCatalog.GetLatestVehicleStatus)]
    [InlineData(AiToolCatalog.GetVehiclesAboveSpeed)]
    [InlineData(AiToolCatalog.GetAnalyticsSummary)]
    [InlineData(AiToolCatalog.GetFleetOverview)]
    public void Catalog_contains_expected_tool(string toolName)
    {
        Assert.True(AiToolCatalog.IsSupported(toolName));
        Assert.True(AiToolCatalog.TryGet(toolName, out var definition));
        Assert.Equal(toolName, definition.Name);
        Assert.NotEmpty(definition.Description);
        Assert.True(definition.Timeout > TimeSpan.Zero);
        Assert.True(definition.MaxResultLines > 0);
    }

    [Fact]
    public void Catalog_exposes_all_seven_tools()
    {
        Assert.Equal(7, AiToolCatalog.All.Count);
    }

    [Fact]
    public void ToJsonMetadata_includes_parameters_and_limits()
    {
        var json = AiToolCatalog.ToJsonMetadata();

        Assert.Contains("GetVehiclesStoppedLongerThan", json);
        Assert.Contains("minutes", json);
        Assert.Contains("MaxResultLines", json);
        Assert.Contains("timeoutSeconds", json);
    }

    [Fact]
    public void IsSupported_rejects_unknown_tool()
    {
        Assert.False(AiToolCatalog.IsSupported("GetWeatherForecast"));
    }
}
