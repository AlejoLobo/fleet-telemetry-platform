// Estado de autenticación compartido sin dependencias React (consumido por telemetry-api).

export type AuthRuntimeMode = "unknown" | "disabled" | "enabled";

export type AuthRuntimeSnapshot = {
  mode: AuthRuntimeMode;
  token: string | null;
  expiresAtIso: string | null;
  tokenExpired: boolean;
};

let snapshot: AuthRuntimeSnapshot = {
  mode: "unknown",
  token: null,
  expiresAtIso: null,
  tokenExpired: false,
};

export function getAuthRuntimeSnapshot(): AuthRuntimeSnapshot {
  return snapshot;
}

export function setAuthRuntimeSnapshot(next: AuthRuntimeSnapshot): void {
  snapshot = next;
}

export function resetAuthRuntimeForTests(): void {
  snapshot = { mode: "unknown", token: null, expiresAtIso: null, tokenExpired: false };
}
