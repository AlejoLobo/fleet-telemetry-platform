using FleetTelemetry.Domain.Common;

namespace FleetTelemetry.Domain.ValueObjects;

// Par latitud/longitud validado.
public sealed class GeoCoordinate : ValueObject
{
    public double Latitude { get; }
    public double Longitude { get; }

    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public static bool TryCreate(double latitude, double longitude, out GeoCoordinate? coordinate, out string? error)
    {
        coordinate = null;
        error = null;

        if (latitude is < -90 or > 90)
        {
            error = "Latitude must be between -90 and 90.";
            return false;
        }

        if (longitude is < -180 or > 180)
        {
            error = "Longitude must be between -180 and 180.";
            return false;
        }

        coordinate = new GeoCoordinate(latitude, longitude);
        return true;
    }

    public static GeoCoordinate Create(double latitude, double longitude) =>
        TryCreate(latitude, longitude, out var coordinate, out var error)
            ? coordinate!
            : throw new ArgumentException(error);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
