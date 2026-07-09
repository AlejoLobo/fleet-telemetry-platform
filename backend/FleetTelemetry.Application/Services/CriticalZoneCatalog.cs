// Catálogo de zonas operativas críticas en Bogotá.
namespace FleetTelemetry.Application.Services;

// Definición de zona con centro y radio en km.
public record CriticalZoneDefinition(
    string Name,
    double CenterLatitude,
    double CenterLongitude,
    double RadiusKm);

/// <summary>
/// Zonas operativas de Bogotá marcadas como críticas para monitoreo de flota.
/// Alineado con las zonas de demo/k6 del frontend.
/// </summary>
// Consulta de pertenencia geográfica a zonas críticas.
public static class CriticalZoneCatalog
{
    public static IReadOnlyList<CriticalZoneDefinition> All { get; } =
    [
        new("Centro", 4.598, -74.075, 1.2),
        new("Kennedy", 4.628, -74.152, 1.5),
        new("Bosa", 4.612, -74.195, 1.4),
        new("San Cristóbal", 4.568, -74.085, 1.3),
        new("Engativá", 4.702, -74.108, 1.2),
    ];

    // Busca la zona crítica más cercana que contenga el punto.
    public static CriticalZoneDefinition? FindZoneAt(double latitude, double longitude)
    {
        CriticalZoneDefinition? best = null;
        var bestDistance = double.MaxValue;

        foreach (var zone in All)
        {
            var distance = GeoMath.DistanceKm(latitude, longitude, zone.CenterLatitude, zone.CenterLongitude);
            if (distance <= zone.RadiusKm && distance < bestDistance)
            {
                best = zone;
                bestDistance = distance;
            }
        }

        return best;
    }

    // Resuelve zona por nombre aproximado.
    public static CriticalZoneDefinition? FindZoneByName(string? zoneName)
    {
        if (string.IsNullOrWhiteSpace(zoneName))
            return null;

        var normalized = Normalize(zoneName);
        return All.FirstOrDefault(z => Normalize(z.Name).Contains(normalized, StringComparison.Ordinal)
            || normalized.Contains(Normalize(z.Name), StringComparison.Ordinal));
    }

    // Indica si las coordenadas caen en alguna zona crítica.
    public static bool IsInAnyCriticalZone(double latitude, double longitude) =>
        FindZoneAt(latitude, longitude) is not null;

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
}
