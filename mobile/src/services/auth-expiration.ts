// Validación centralizada de expiración de tokens JWT.

export type ExpirationParseResult =
  | { valid: true; expiresAtMs: number }
  | { valid: false; reason: "missing" | "empty" | "invalid" | "expired" };

export function parseExpiration(
  expiresAtIso: string | null | undefined,
  nowMs: number = Date.now(),
): ExpirationParseResult {
  if (expiresAtIso === null || expiresAtIso === undefined) {
    return { valid: false, reason: "missing" };
  }
  if (expiresAtIso.trim() === "") {
    return { valid: false, reason: "empty" };
  }
  const expiresAtMs = Date.parse(expiresAtIso);
  if (!Number.isFinite(expiresAtMs)) {
    return { valid: false, reason: "invalid" };
  }
  if (expiresAtMs <= nowMs) {
    return { valid: false, reason: "expired" };
  }
  return { valid: true, expiresAtMs };
}

export function isExpirationActive(expiresAtIso: string | null | undefined, nowMs?: number): boolean {
  return parseExpiration(expiresAtIso, nowMs).valid;
}
