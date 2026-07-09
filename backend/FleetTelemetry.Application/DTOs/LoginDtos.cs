// DTOs de autenticación JWT.
namespace FleetTelemetry.Application.DTOs;

// Credenciales de acceso.
public record LoginRequest(string Username, string Password);

// Token emitido y tiempo de expiración.
public record LoginResponse(string Token, int ExpiresInMinutes);
