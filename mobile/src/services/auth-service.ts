import { getApiBaseUrl } from "@/config/env";
import { parseExpiration } from "@/services/auth-expiration";
import {
  setAuthRuntimeSnapshot,
  resetAuthRuntimeForTests,
  type AuthRuntimeSnapshot,
} from "@/services/auth-runtime";
import type { AuthTokenStore } from "@/services/auth-token-store";
import { InMemoryAuthTokenStore, SecureAuthTokenStore } from "@/services/auth-token-store";
import {
  isDeviceTelemetrySyncEligible,
  parseJwtClaims,
  type JwtSessionKind,
} from "@/services/jwt-claims";
import type { AuthSessionStatus, LoginResponse } from "@/types/auth";

export type AuthSessionSnapshot = {
  status: AuthSessionStatus;
  enabled: boolean;
  username: string | null;
  statusMessage: string | null;
  sessionKind: JwtSessionKind;
  deviceId: string | null;
  permissions: string[];
};

type AuthListener = (snapshot: AuthSessionSnapshot) => void;

const DEFAULT_TIMEOUT_MS = 15_000;

let tokenStore: AuthTokenStore = new SecureAuthTokenStore();
let session: AuthSessionSnapshot = {
  status: "checking",
  enabled: false,
  username: null,
  statusMessage: null,
  sessionKind: "none",
  deviceId: null,
  permissions: [],
};
const listeners = new Set<AuthListener>();
let cachedToken: string | null = null;
let cachedExpiresAt: string | null = null;

export function configureAuthTokenStore(store: AuthTokenStore): void {
  tokenStore = store;
}

export function getAuthSessionSnapshot(): AuthSessionSnapshot {
  return session;
}

export function subscribeAuthSession(listener: AuthListener): () => void {
  listeners.add(listener);
  listener(session);
  return () => listeners.delete(listener);
}

function claimsFromToken(token: string | null): Pick<
  AuthSessionSnapshot,
  "sessionKind" | "deviceId" | "permissions" | "username"
> {
  const claims = parseJwtClaims(token);
  return {
    sessionKind: claims.sessionKind,
    deviceId: claims.deviceId,
    permissions: claims.permissions,
    username: claims.username,
  };
}

function emptySessionFields(): Pick<
  AuthSessionSnapshot,
  "sessionKind" | "deviceId" | "permissions" | "username"
> {
  return { sessionKind: "none", deviceId: null, permissions: [], username: null };
}

function buildRuntimeSnapshot(next: AuthSessionSnapshot): AuthRuntimeSnapshot {
  if (next.status === "checking" || next.status === "status_error") {
    return { mode: "unknown", token: null, expiresAtIso: null, tokenExpired: false };
  }
  if (next.status === "auth_disabled") {
    return { mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false };
  }

  const expiration = parseExpiration(cachedExpiresAt);
  const tokenExpired = next.status === "session_expired" || !expiration.valid;

  return {
    mode: "enabled",
    token: next.status === "authenticated" ? cachedToken : null,
    expiresAtIso: cachedExpiresAt,
    tokenExpired,
  };
}

function publish(next: AuthSessionSnapshot): void {
  session = next;
  setAuthRuntimeSnapshot(buildRuntimeSnapshot(next));
  listeners.forEach((listener) => listener(next));
}

export function getActiveToken(): string | null {
  const runtime = buildRuntimeSnapshot(session);
  if (runtime.mode !== "enabled" || runtime.tokenExpired) return null;
  if (session.status !== "authenticated") return null;
  if (!cachedToken) return null;
  if (!parseExpiration(cachedExpiresAt).valid) return null;
  return cachedToken;
}

export function getCachedExpiresAt(): string | null {
  return cachedExpiresAt;
}

/** True solo con auth deshabilitada o token de dispositivo válido para el DeviceId local. */
export function canSyncTelemetryForDevice(localDeviceId: string | null | undefined): boolean {
  if (session.status === "auth_disabled") return true;
  if (session.status !== "authenticated") return false;
  return isDeviceTelemetrySyncEligible(parseJwtClaims(cachedToken), localDeviceId);
}

function parseAuthStatusPayload(body: unknown): { enabled: boolean } | null {
  if (typeof body !== "object" || body === null) return null;
  if (!("enabled" in body)) return null;
  const enabled = (body as { enabled: unknown }).enabled;
  if (typeof enabled !== "boolean") return null;
  return { enabled };
}

async function fetchAuthStatus(): Promise<{ enabled: boolean } | null> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS);
  try {
    const response = await fetch(`${getApiBaseUrl()}/api/auth/status`, {
      method: "GET",
      signal: controller.signal,
    });
    if (!response.ok) return null;
    const body = await response.json();
    return parseAuthStatusPayload(body);
  } catch {
    return null;
  } finally {
    clearTimeout(timeout);
  }
}

async function clearInvalidStoredToken(): Promise<void> {
  await tokenStore.clear();
  cachedToken = null;
  cachedExpiresAt = null;
}

export async function initializeAuthSession(): Promise<AuthSessionSnapshot> {
  publish({
    status: "checking",
    enabled: false,
    statusMessage: null,
    ...emptySessionFields(),
  });
  const status = await fetchAuthStatus();
  if (!status) {
    publish({
      status: "status_error",
      enabled: false,
      statusMessage: "Error consultando auth status",
      ...emptySessionFields(),
    });
    return session;
  }

  if (!status.enabled) {
    cachedToken = null;
    cachedExpiresAt = null;
    publish({
      status: "auth_disabled",
      enabled: false,
      statusMessage: "Autenticación deshabilitada",
      ...emptySessionFields(),
    });
    return session;
  }

  const stored = await tokenStore.load();
  if (!stored || !parseExpiration(stored.expiresAtIso).valid) {
    await clearInvalidStoredToken();
    publish({
      status: stored ? "session_expired" : "auth_required",
      enabled: true,
      statusMessage: stored ? "Sesión vencida" : "Login requerido",
      ...emptySessionFields(),
    });
    return session;
  }

  cachedToken = stored.token;
  cachedExpiresAt = stored.expiresAtIso;
  const claims = claimsFromToken(stored.token);
  // Solo restauramos sesión autenticada si el JWT es de dispositivo.
  if (claims.sessionKind !== "device" || !claims.permissions.includes("telemetry:write")) {
    await clearInvalidStoredToken();
    publish({
      status: "auth_required",
      enabled: true,
      statusMessage: "Se requiere enrolamiento de dispositivo",
      ...emptySessionFields(),
    });
    return session;
  }

  publish({
    status: "authenticated",
    enabled: true,
    statusMessage: null,
    ...claims,
  });
  return session;
}

/**
 * Enrolamiento MVP: intercambia credenciales + DeviceId por JWT de dispositivo.
 * No usa el token de operador para sincronizar telemetría.
 */
export async function enrollDevice(
  deviceId: string,
  username: string,
  password: string,
): Promise<AuthSessionSnapshot> {
  const normalizedDeviceId = deviceId.trim();
  if (!normalizedDeviceId) {
    throw new Error("DeviceId no disponible para enrolamiento");
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS);
  try {
    const response = await fetch(`${getApiBaseUrl()}/api/auth/device-token`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        deviceId: normalizedDeviceId,
        username,
        password,
      }),
      signal: controller.signal,
    });
    if (!response.ok) {
      const message = await response.text();
      throw new Error(message || `Enrolamiento HTTP ${response.status}`);
    }
    const body = (await response.json()) as LoginResponse & { deviceId?: string };
    const expiresAtIso = new Date(Date.now() + body.expiresInMinutes * 60_000).toISOString();
    await tokenStore.save({ token: body.token, expiresAtIso });
    cachedToken = body.token;
    cachedExpiresAt = expiresAtIso;
    const claims = claimsFromToken(body.token);
    if (
      claims.sessionKind !== "device"
      || !claims.permissions.includes("telemetry:write")
      || !claims.deviceId
      || claims.deviceId.toLowerCase() !== normalizedDeviceId.toLowerCase()
    ) {
      await clearInvalidStoredToken();
      throw new Error("El token emitido no es un JWT de dispositivo válido");
    }

    publish({
      status: "authenticated",
      enabled: true,
      statusMessage: null,
      ...claims,
      username: username.trim() || claims.username,
    });
    return session;
  } finally {
    clearTimeout(timeout);
  }
}

/** @deprecated Preferir enrollDevice para la app móvil. Conservado para tests de operador. */
export async function login(username: string, password: string): Promise<AuthSessionSnapshot> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS);
  try {
    const response = await fetch(`${getApiBaseUrl()}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password }),
      signal: controller.signal,
    });
    if (!response.ok) {
      const message = await response.text();
      throw new Error(message || `Login HTTP ${response.status}`);
    }
    const body = (await response.json()) as LoginResponse;
    const expiresAtIso = new Date(Date.now() + body.expiresInMinutes * 60_000).toISOString();
    await tokenStore.save({ token: body.token, expiresAtIso });
    cachedToken = body.token;
    cachedExpiresAt = expiresAtIso;
    const claims = claimsFromToken(body.token);
    publish({
      status: "authenticated",
      enabled: true,
      statusMessage: null,
      ...claims,
      username: username.trim() || claims.username,
    });
    return session;
  } finally {
    clearTimeout(timeout);
  }
}

export async function logout(): Promise<AuthSessionSnapshot> {
  await tokenStore.clear();
  cachedToken = null;
  cachedExpiresAt = null;
  if (session.status === "auth_disabled") {
    publish({
      status: "auth_disabled",
      enabled: false,
      statusMessage: null,
      ...emptySessionFields(),
    });
    return session;
  }
  publish({
    status: "auth_required",
    enabled: true,
    statusMessage: "Login requerido",
    ...emptySessionFields(),
  });
  return session;
}

export async function handleUnauthorizedFromApi(): Promise<void> {
  await clearInvalidStoredToken();
  if (session.status === "auth_disabled") return;
  publish({
    status: "auth_required",
    enabled: true,
    statusMessage: "Sesión vencida",
    ...emptySessionFields(),
  });
}

export async function handleSessionExpiredBeforeRequest(): Promise<void> {
  const previousUsername = session.username;
  await clearInvalidStoredToken();
  publish({
    status: "session_expired",
    enabled: true,
    username: previousUsername,
    statusMessage: "Sesión vencida",
    sessionKind: "none",
    deviceId: null,
    permissions: [],
  });
}

export function markForbiddenFromApi(message?: string): void {
  publish({
    status: "forbidden",
    enabled: session.enabled,
    username: session.username,
    statusMessage: message ?? "Permiso insuficiente",
    sessionKind: session.sessionKind,
    deviceId: session.deviceId,
    permissions: session.permissions,
  });
}

export function resetAuthServiceForTests(): void {
  tokenStore = new InMemoryAuthTokenStore();
  cachedToken = null;
  cachedExpiresAt = null;
  session = {
    status: "checking",
    enabled: false,
    statusMessage: null,
    ...emptySessionFields(),
  };
  listeners.clear();
  resetAuthRuntimeForTests();
}
