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

    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        if (!_authOptions.Enabled)
            return BadRequest(new { error = "Autenticación deshabilitada en este entorno." });

        if (request.Username != _authOptions.DemoUsername || request.Password != _authOptions.DemoPassword)
            return Unauthorized(new { error = "Credenciales inválidas." });

        var token = _jwtTokenService.GenerateToken(request.Username);
        return Ok(new LoginResponse(token, _authOptions.TokenExpirationMinutes));
    }

    [HttpGet("status")]
    public IActionResult Status() =>
        Ok(new { enabled = _authOptions.Enabled });
}
