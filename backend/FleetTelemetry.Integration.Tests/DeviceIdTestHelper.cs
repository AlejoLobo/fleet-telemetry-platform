using System.Security.Cryptography;
using System.Text;

namespace FleetTelemetry.Integration.Tests;

// Guids deterministas a partir de seeds legibles (p. ej. "VH-AAA") para tests.
internal static class DeviceIdTestHelper
{
    public static Guid CreateDeterministicGuid(string seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }

    public static string ToStorage(Guid deviceId) => deviceId.ToString("D");

    public static string ToStorage(string seed) => CreateDeterministicGuid(seed).ToString("D");
}
