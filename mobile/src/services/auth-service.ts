import { getApiBaseUrl } from "@/config/env";
import { setAuthRuntimeSnapshot, resetAuthRuntimeForTests } from "@/services/auth-runtime";
import type { AuthTokenStore } from "@/services/auth-token-store";
import { InMemoryAuthTokenStore, SecureAuthTokenStore } from "@/services/auth-token-store";
import type { AuthSessionStatus, AuthStatusResponse, LoginResponse } from "@/types/auth";

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

function publish(next: AuthSessionSnapshot): void {
  session = next;
  const token = getActiveToken();
  setAuthRuntimeSnapshot({
    enabled: next.enabled,
    token,
    tokenExpired: next.status === "session_expired",
  });
  listeners.forEach((listener) => listener(next));
}

function isExpired(expiresAtIso: string): boolean {
  return new Date(expiresAtIso).getTime() <= Date.now();
}

export function getActiveToken(): string | null {
  if (!session.enabled) return null;
  if (session.status !== "authenticated") return null;
  return cachedToken;
}

async function fetchAuthStatus(): Promise<AuthStatusResponse> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS);
  try {
    const response = await fetch(`${getApiBaseUrl()}/api/auth/status`, {
      method: "GET",
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`Auth status HTTP ${response.status}`);
    return (await response.json()) as AuthStatusResponse;
  } finally {
    clearTimeout(timeout);
  }
}

export async function initializeAuthSession(): Promise<AuthSessionSnapshot> {
  publish({ status: "checking", enabled: false, username: null, statusMessage: null });
  try {
    const status = await fetchAuthStatus();
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
  } catch (error) {
    publish({
      status: "status_error",
      enabled: false,
      username: null,
      statusMessage: error instanceof Error ? error.message : "Error consultando auth status",
    });
    return session;
  }
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
  if (!session.enabled) {
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
  if (!session.enabled) return;
  publish({ status: "auth_required", enabled: true, username: null, statusMessage: "Sesión vencida" });
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
