// Estado de autenticación compartido sin dependencias React (consumido por telemetry-api).

export type AuthRuntimeSnapshot = {
  enabled: boolean;
  token: string | null;
  tokenExpired: boolean;
};

let snapshot: AuthRuntimeSnapshot = {
  enabled: false,
  token: null,
  tokenExpired: false,
};

export function getAuthRuntimeSnapshot(): AuthRuntimeSnapshot {
  return snapshot;
}

export function setAuthRuntimeSnapshot(next: AuthRuntimeSnapshot): void {
  snapshot = next;
}

export function resetAuthRuntimeForTests(): void {
  snapshot = { enabled: false, token: null, tokenExpired: false };
}
