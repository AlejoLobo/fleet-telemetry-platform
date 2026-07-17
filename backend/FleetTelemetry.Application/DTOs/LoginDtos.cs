namespace FleetTelemetry.Application.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, int ExpiresInMinutes);

/// <summary>
/// Enrolamiento MVP: credenciales válidas + DeviceId → token de dispositivo.
/// En producción debería reemplazarse por mTLS, attestation o enrollment firmado.
/// </summary>
public record DeviceTokenRequest(Guid DeviceId, string Username, string Password);

public record DeviceTokenResponse(string Token, int ExpiresInMinutes, Guid DeviceId);
