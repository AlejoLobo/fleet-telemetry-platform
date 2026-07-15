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

    [Theory]
    [InlineData(30, true)]
    [InlineData(46, false)]
    public void Resolve_con_ventana_en_segundos_respeta_umbral(int ageSeconds, bool expectOnline)
    {
        var now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        var lastSeen = now.AddSeconds(-ageSeconds);

        var status = VehicleConnectivityStatus.Resolve(lastSeen, now, TimeSpan.FromSeconds(45));

        Assert.Equal(
            expectOnline ? VehicleConnectivityStatus.Online : VehicleConnectivityStatus.Offline,
            status);
    }
}
