// Estados de sesión de autenticación móvil

export type AuthSessionStatus =
  | "checking"
  | "auth_disabled"
  | "auth_required"
  | "authenticated"
  | "session_expired"
  | "forbidden"
  | "status_error";

export type StoredAuthToken = {
  token: string;
  expiresAtIso: string;
};

export type AuthStatusResponse = {
  enabled: boolean;
};

export type LoginResponse = {
  token: string;
  expiresInMinutes: number;
};
