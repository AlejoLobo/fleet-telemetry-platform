using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FleetTelemetry.Infrastructure.Auth;

public class JwtTokenService
{
    private readonly AuthOptions _options;

    public JwtTokenService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Alias de compatibilidad: token de operador sin device:manage.</summary>
    public string GenerateToken(string username) => GenerateOperatorToken(username);

    public string GenerateOperatorToken(string username, bool canManageDevices = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, "operator")
        };
        foreach (var permission in AuthorizationPermissions.OperatorPermissions)
            claims.Add(new Claim(AuthorizationPermissions.ClaimType, permission));

        if (canManageDevices)
            claims.Add(new Claim(AuthorizationPermissions.ClaimType, AuthorizationPermissions.DeviceManage));

        return WriteToken(claims);
    }

    public string GenerateDeviceToken(Guid deviceId)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));

        var claims = new List<Claim>
        {
            new(AuthorizationPermissions.DeviceIdClaimType, deviceId.ToString("D")),
            new(ClaimTypes.Role, "device"),
            new(AuthorizationPermissions.ClaimType, AuthorizationPermissions.TelemetryWrite)
        };

        return WriteToken(claims);
    }

    private string WriteToken(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_options.TokenExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
