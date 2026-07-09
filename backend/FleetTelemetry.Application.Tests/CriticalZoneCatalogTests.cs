using FleetTelemetry.Application.Services;

// Pruebas del catálogo de zonas críticas.
namespace FleetTelemetry.Application.Tests;

public class CriticalZoneCatalogTests
{
    [Fact]
    public void FindZoneAt_matches_centro_coordinates()
    {
        var zone = CriticalZoneCatalog.FindZoneAt(4.598, -74.075);
        Assert.NotNull(zone);
        Assert.Equal("Centro", zone!.Name);
    }

    [Fact]
    public void FindZoneAt_returns_null_outside_zones()
    {
        var zone = CriticalZoneCatalog.FindZoneAt(4.0, -75.0);
        Assert.Null(zone);
    }

    [Fact]
    public void FindZoneByName_is_case_insensitive()
    {
        var zone = CriticalZoneCatalog.FindZoneByName("kennedy");
        Assert.NotNull(zone);
        Assert.Equal("Kennedy", zone!.Name);
    }
}
