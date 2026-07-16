using FleetTelemetry.Application.DTOs;
using FleetTelemetry.Infrastructure.Auth;
using FleetTelemetry.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _authOptions;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        IOptions<AuthOptions> authOptions,
        JwtTokenService jwtTokenService,
        IWebHostEnvironment environment)
    {
        _authOptions = authOptions.Value;
        _jwtTokenService = jwtTokenService;
        _environment = environment;
    }

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
    /// Enrolamiento MVP demo: emite JWT de dispositivo ligado a DeviceId.
    /// No es attestation. Requiere Auth:AllowDemoDeviceEnrollment=true y no Production.
    /// </summary>
    [HttpPost("device-token")]
    public ActionResult<DeviceTokenResponse> IssueDeviceToken([FromBody] DeviceTokenRequest request)
    {
        if (!_authOptions.Enabled)
            return BadRequest(new { error = "Autenticación deshabilitada en este entorno." });

        if (!_authOptions.AllowDemoDeviceEnrollment)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Enrolamiento demo deshabilitado. Configure Auth:AllowDemoDeviceEnrollment solo en Development/Demo.",
            });

        if (_environment.IsProduction())
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Enrolamiento demo prohibido en Production. Use attestation/mTLS/enrollment firmado.",
            });

        if (request.DeviceId == Guid.Empty)
            return BadRequest(new { error = "DeviceId debe ser un UUID no vacío." });

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized(new { error = "Credenciales inválidas." });

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
