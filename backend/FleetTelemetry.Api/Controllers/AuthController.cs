using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _authOptions;
    private readonly JwtTokenService _jwtTokenService;

    public AuthController(IOptions<AuthOptions> authOptions, JwtTokenService jwtTokenService)
    {
        _authOptions = authOptions.Value;
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// Login de operador (portal). No incluye telemetry:write.
    /// Admin demos puede recibir device:manage; el operador común no.
    /// </summary>
    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        if (!_authOptions.Enabled)
            return BadRequest(new { error = "Autenticación deshabilitada en este entorno." });

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized(new { error = "Credenciales inválidas." });

        if (IsAdminCredentials(request.Username, request.Password))
        {
            var adminToken = _jwtTokenService.GenerateOperatorToken(request.Username.Trim(), canManageDevices: true);
            return Ok(new LoginResponse(adminToken, _authOptions.TokenExpirationMinutes));
        }

        if (IsOperatorCredentials(request.Username, request.Password))
        {
            var operatorToken = _jwtTokenService.GenerateOperatorToken(request.Username.Trim(), canManageDevices: false);
            return Ok(new LoginResponse(operatorToken, _authOptions.TokenExpirationMinutes));
        }

        return Unauthorized(new { error = "Credenciales inválidas." });
    }

    /// <summary>
    /// Emite un JWT de dispositivo ligado a DeviceId (role=device, telemetry:write, device_id).
    /// MVP: requiere credenciales demo válidas (operador o admin). No es attestation de producción.
    /// </summary>
    [HttpPost("device-token")]
    public ActionResult<DeviceTokenResponse> IssueDeviceToken([FromBody] DeviceTokenRequest request)
    {
        if (!_authOptions.Enabled)
            return BadRequest(new { error = "Autenticación deshabilitada en este entorno." });

        if (request.DeviceId == Guid.Empty)
            return BadRequest(new { error = "DeviceId debe ser un UUID no vacío." });

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized(new { error = "Credenciales inválidas." });

        // Autorización explícita MVP: solo cuentas demo configuradas pueden enrolar.
        var authorized =
            IsOperatorCredentials(request.Username, request.Password)
            || IsAdminCredentials(request.Username, request.Password);

        if (!authorized)
            return Unauthorized(new { error = "Credenciales inválidas." });

        var token = _jwtTokenService.GenerateDeviceToken(request.DeviceId);
        return Ok(new DeviceTokenResponse(token, _authOptions.TokenExpirationMinutes, request.DeviceId));
    }

    [HttpGet("status")]
    public IActionResult Status() =>
        Ok(new { enabled = _authOptions.Enabled });

    private bool IsOperatorCredentials(string username, string password) =>
        !string.IsNullOrEmpty(_authOptions.DemoPassword)
        && string.Equals(username.Trim(), _authOptions.DemoUsername, StringComparison.Ordinal)
        && string.Equals(password, _authOptions.DemoPassword, StringComparison.Ordinal);

    private bool IsAdminCredentials(string username, string password) =>
        !string.IsNullOrEmpty(_authOptions.AdminPassword)
        && string.Equals(username.Trim(), _authOptions.AdminUsername, StringComparison.Ordinal)
        && string.Equals(password, _authOptions.AdminPassword, StringComparison.Ordinal);
}
