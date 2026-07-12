import { getApiBaseUrl } from "@/config/env";
import { setAuthRuntimeSnapshot, resetAuthRuntimeForTests, type AuthRuntimeSnapshot } from "@/services/auth-runtime";
import type { AuthTokenStore } from "@/services/auth-token-store";
import { InMemoryAuthTokenStore, SecureAuthTokenStore } from "@/services/auth-token-store";
import type { AuthSessionStatus, LoginResponse } from "@/types/auth";

export type AuthSessionSnapshot = {
  status: AuthSessionStatus;
  enabled: boolean;
  username: string | null;
  statusMessage: string | null;
};

type AuthListener = (snapshot: AuthSessionSnapshot) => void;

const DEFAULT_TIMEOUT_MS = 15_000;

let tokenStore: AuthTokenStore = new SecureAuthTokenStore();
let session: AuthSessionSnapshot = {
  status: "checking",
  enabled: false,
  username: null,
  statusMessage: null,
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

function isExpired(expiresAtIso: string): boolean {
  return new Date(expiresAtIso).getTime() <= Date.now();
}

function buildRuntimeSnapshot(next: AuthSessionSnapshot): AuthRuntimeSnapshot {
  if (next.status === "checking" || next.status === "status_error") {
    return { mode: "unknown", token: null, expiresAtIso: null, tokenExpired: false };
  }
  if (next.status === "auth_disabled") {
    return { mode: "disabled", token: null, expiresAtIso: null, tokenExpired: false };
  }

  const tokenExpired =
    next.status === "session_expired"
    || (cachedExpiresAt !== null && isExpired(cachedExpiresAt));

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
  return cachedToken;
}

export function getCachedExpiresAt(): string | null {
  return cachedExpiresAt;
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

export async function initializeAuthSession(): Promise<AuthSessionSnapshot> {
  publish({ status: "checking", enabled: false, username: null, statusMessage: null });
  const status = await fetchAuthStatus();
  if (!status) {
    publish({
      status: "status_error",
      enabled: false,
      username: null,
      statusMessage: "Error consultando auth status",
    });
    return session;
  }

  if (!status.enabled) {
    cachedToken = null;
    cachedExpiresAt = null;
    publish({
      status: "auth_disabled",
      enabled: false,
      username: null,
      statusMessage: "Autenticación deshabilitada",
    });
    return session;
  }

  const stored = await tokenStore.load();
  if (!stored || isExpired(stored.expiresAtIso)) {
    await tokenStore.clear();
    cachedToken = null;
    cachedExpiresAt = null;
    publish({
      status: stored ? "session_expired" : "auth_required",
      enabled: true,
      username: null,
      statusMessage: stored ? "Sesión vencida" : "Login requerido",
    });
    return session;
  }

  cachedToken = stored.token;
  cachedExpiresAt = stored.expiresAtIso;
  publish({
    status: "authenticated",
    enabled: true,
    username: null,
    statusMessage: null,
  });
  return session;
}

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
    publish({
      status: "authenticated",
      enabled: true,
      username,
      statusMessage: null,
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
    publish({ status: "auth_disabled", enabled: false, username: null, statusMessage: null });
    return session;
  }
  publish({ status: "auth_required", enabled: true, username: null, statusMessage: "Login requerido" });
  return session;
}

export async function handleUnauthorizedFromApi(): Promise<void> {
  await tokenStore.clear();
  cachedToken = null;
  cachedExpiresAt = null;
  if (session.status === "auth_disabled") return;
  publish({ status: "auth_required", enabled: true, username: null, statusMessage: "Sesión vencida" });
}

export async function handleSessionExpiredBeforeRequest(): Promise<void> {
  await tokenStore.clear();
  cachedToken = null;
  cachedExpiresAt = null;
  publish({
    status: "session_expired",
    enabled: true,
    username: session.username,
    statusMessage: "Sesión vencida",
  });
}

export function markForbiddenFromApi(message?: string): void {
  publish({
    status: "forbidden",
    enabled: session.enabled,
    username: session.username,
    statusMessage: message ?? "Permiso insuficiente",
  });
}

export function resetAuthServiceForTests(): void {
  tokenStore = new InMemoryAuthTokenStore();
  cachedToken = null;
  cachedExpiresAt = null;
  session = { status: "checking", enabled: false, username: null, statusMessage: null };
  listeners.clear();
  resetAuthRuntimeForTests();
}
