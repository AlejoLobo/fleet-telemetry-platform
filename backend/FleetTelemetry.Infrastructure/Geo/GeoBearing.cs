using FleetTelemetry.Infrastructure.Persistence.Entities;

namespace FleetTelemetry.Infrastructure.Geo;

public static class GeoBearing
{
    /// <summary>
    /// Rumbo en grados (0=norte, 90=este) desde el punto anterior al actual.
    /// </summary>
    public static double? ComputeHeadingDegrees(TelemetryEventRecord? from, TelemetryEventRecord? to)
    {
        if (from is null || to is null) return null;

        var lat1 = ToRadians(from.Latitude);
        var lat2 = ToRadians(to.Latitude);
        var dLng = ToRadians(to.Longitude - from.Longitude);

        var y = Math.Sin(dLng) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLng);
        var bearing = Math.Atan2(y, x);

        return (ToDegrees(bearing) + 360) % 360;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    private static double ToDegrees(double radians) => radians * 180 / Math.PI;
}
