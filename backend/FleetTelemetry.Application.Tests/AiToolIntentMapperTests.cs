using System.Text.Json;
using FleetTelemetry.Application.Services;

namespace FleetTelemetry.Application.Tests;

public class AiToolIntentMapperTests
{
    [Fact]
    public void FromToolCall_maps_fleet_overview()
    {
        var intent = AiToolIntentMapper.FromToolCall(AiToolCatalog.GetFleetOverview, new Dictionary<string, JsonElement>());
        Assert.Equal(AiQueryIntent.FleetOverview, intent.Intent);
    }

    [Fact]
    public void FromToolCall_maps_vehicle_status_with_id()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["vehicleId"] = JsonSerializer.SerializeToElement("VH-001")
        };
        var intent = AiToolIntentMapper.FromToolCall(AiToolCatalog.GetLatestVehicleStatus, args);
        Assert.Equal(AiQueryIntent.VehicleStatus, intent.Intent);
        Assert.Equal("VH-001", intent.VehicleId);
    }

    [Fact]
    public void FromToolCall_returns_unsupported_for_unknown_tool()
    {
        var intent = AiToolIntentMapper.FromToolCall("GetWeather", new Dictionary<string, JsonElement>());
        Assert.Equal(AiQueryIntent.UnsupportedQuery, intent.Intent);
    }
}
