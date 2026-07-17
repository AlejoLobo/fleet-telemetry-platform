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
            ["deviceId"] = JsonSerializer.SerializeToElement(Guid.Parse("11111111-1111-1111-1111-111111111111"))
        };
        var intent = AiToolIntentMapper.FromToolCall(AiToolCatalog.GetLatestVehicleStatus, args);
        Assert.Equal(AiQueryIntent.VehicleStatus, intent.Intent);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), intent.DeviceId);
    }

    [Fact]
    public void FromToolCall_returns_unsupported_for_unknown_tool()
    {
        var intent = AiToolIntentMapper.FromToolCall("GetWeather", new Dictionary<string, JsonElement>());
        Assert.Equal(AiQueryIntent.UnsupportedQuery, intent.Intent);
    }
}
