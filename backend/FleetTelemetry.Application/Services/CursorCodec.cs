using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FleetTelemetry.Application.DTOs;

namespace FleetTelemetry.Application.Services;

// Codifica y decodifica cursores opacos con validación estricta.
public static class CursorCodec
{
    public const int MaxCursorLength = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Encode<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static T Decode<T>(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            throw new InvalidCursorException("Cursor inválido.");

        if (cursor.Length > MaxCursorLength)
            throw new InvalidCursorException("Cursor inválido.");

        byte[] bytes;
        try
        {
            bytes = Base64UrlDecode(cursor);
        }
        catch (FormatException)
        {
            throw new InvalidCursorException("Cursor inválido.");
        }

        if (bytes.Length > MaxCursorLength)
            throw new InvalidCursorException("Cursor inválido.");

        try
        {
            var payload = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
            if (payload is null)
                throw new InvalidCursorException("Cursor inválido.");
            return payload;
        }
        catch (JsonException)
        {
            throw new InvalidCursorException("Cursor inválido.");
        }
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string encoded)
    {
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}

public sealed class InvalidCursorException : Exception
{
    public InvalidCursorException(string message) : base(message)
    {
    }
}
