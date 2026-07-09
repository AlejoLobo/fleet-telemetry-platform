namespace FleetTelemetry.Application.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, int ExpiresInMinutes);
