using FleetTelemetry.Application.Services;

namespace FleetTelemetry.Application.Tests;

public class VehicleConnectivityStatusTests
{
    [Fact]
    public void Evento_reciente_velocidad_cero_es_online()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var lastSeen = now.AddMinutes(-2);

        var status = VehicleConnectivityStatus.Resolve(lastSeen, now, onlineThresholdMinutes: 5);

        Assert.Equal(VehicleConnectivityStatus.Online, status);
    }

    [Fact]
    public void Evento_reciente_en_movimiento_es_online()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var lastSeen = now.AddMinutes(-1);

        var status = VehicleConnectivityStatus.Resolve(lastSeen, now, onlineThresholdMinutes: 5);

        Assert.Equal(VehicleConnectivityStatus.Online, status);
    }

    [Fact]
    public void Evento_antiguo_es_offline()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var lastSeen = now.AddMinutes(-10);

        var status = VehicleConnectivityStatus.Resolve(lastSeen, now, onlineThresholdMinutes: 5);

        Assert.Equal(VehicleConnectivityStatus.Offline, status);
    }
}
