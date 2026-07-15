/** Decodifica claims del JWT sin verificar firma (la firma la valida el API). */

export type JwtSessionKind = "none" | "operator" | "device";

export type ParsedJwtClaims = {
  sessionKind: JwtSessionKind;
  deviceId: string | null;
  permissions: string[];
  role: string | null;
  username: string | null;
};

const ROLE_CLAIM =
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const NAME_CLAIM =
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/name";

function decodeBase64Url(input: string): string {
  const normalized = input.replace(/-/g, "+").replace(/_/g, "/");
  const pad = normalized.length % 4 === 0 ? "" : "=".repeat(4 - (normalized.length % 4));
  if (typeof globalThis.atob === "function") {
    return globalThis.atob(normalized + pad);
  }
  return Buffer.from(normalized + pad, "base64").toString("utf8");
}

function asStringArray(value: unknown): string[] {
  if (typeof value === "string") return [value];
  if (Array.isArray(value)) {
    return value.filter((item): item is string => typeof item === "string");
  }
  return [];
}

export function parseJwtClaims(token: string | null | undefined): ParsedJwtClaims {
  const empty: ParsedJwtClaims = {
    sessionKind: "none",
    deviceId: null,
    permissions: [],
    role: null,
    username: null,
  };

  if (!token || typeof token !== "string") return empty;
  const parts = token.split(".");
  if (parts.length < 2) return empty;

  try {
    const payload = JSON.parse(decodeBase64Url(parts[1])) as Record<string, unknown>;
    const permissions = asStringArray(payload.permission);
    const roles = [
      ...asStringArray(payload.role),
      ...asStringArray(payload[ROLE_CLAIM]),
    ];
    const role = roles[0] ?? null;
    const deviceIdRaw = payload.device_id;
    const deviceId =
      typeof deviceIdRaw === "string" && deviceIdRaw.trim().length > 0
        ? deviceIdRaw.trim()
        : null;
    const usernameRaw = payload.unique_name ?? payload[NAME_CLAIM] ?? payload.name;
    const username =
      typeof usernameRaw === "string" && usernameRaw.trim().length > 0
        ? usernameRaw.trim()
        : null;

    let sessionKind: JwtSessionKind = "none";
    if (role === "device" || (deviceId && permissions.includes("telemetry:write"))) {
      sessionKind = "device";
    } else if (role === "operator" || permissions.length > 0) {
      sessionKind = "operator";
    }

    return { sessionKind, deviceId, permissions, role, username };
  } catch {
    return empty;
  }
}

/** Elegibilidad de sync de telemetría para un DeviceId local. */
export function isDeviceTelemetrySyncEligible(
  claims: ParsedJwtClaims,
  localDeviceId: string | null | undefined,
): boolean {
  if (claims.sessionKind !== "device") return false;
  if (!claims.permissions.includes("telemetry:write")) return false;
  if (!claims.deviceId || !localDeviceId) return false;
  return claims.deviceId.trim().toLowerCase() === localDeviceId.trim().toLowerCase();
}
