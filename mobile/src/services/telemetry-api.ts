import { getApiBaseUrl } from "@/config/env";
import { parseExpiration } from "@/services/auth-expiration";
import { handleSessionExpiredBeforeRequest } from "@/services/auth-service";
import { getAuthRuntimeSnapshot } from "@/services/auth-runtime";
import type { TelemetryEventPayload } from "@/types/telemetry";

const DEFAULT_TIMEOUT_MS = 30_000;
const MAX_ERROR_BODY_LENGTH = 240;

export type TelemetryApiErrorCategory =
  | "auth_required"
  | "forbidden"
  | "timeout"
  | "network"
  | "validation"
  | "transient"
  | "protocol"
  | "payload_too_large";

export class TelemetryApiError extends Error {
  readonly status: number;
  readonly category: TelemetryApiErrorCategory;
  readonly retryAfterSeconds?: number;
  readonly sanitizedMessage: string;

  constructor(status: number, category: TelemetryApiErrorCategory, message: string, retryAfterSeconds?: number) {
    super(message);
    this.name = "TelemetryApiError";
    this.status = status;
    this.category = category;
    this.retryAfterSeconds = retryAfterSeconds;
    this.sanitizedMessage = sanitizeErrorText(message);
  }
}

export function sanitizeErrorText(raw: string): string {
  const redacted = raw
    .replace(/Bearer\s+\S+/gi, "[redacted]")
    .replace(/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/g, "[redacted]")
    .replace(/<[^>]+>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  if (redacted.length <= MAX_ERROR_BODY_LENGTH) return redacted;
  return `${redacted.slice(0, MAX_ERROR_BODY_LENGTH)}...`;
}

export function categorizeHttpStatus(status: number): TelemetryApiErrorCategory {
  if (status === 401) return "auth_required";
  if (status === 403) return "forbidden";
  if (status === 408) return "timeout";
  if (status === 0) return "network";
  if (status === 400 || status === 422) return "validation";
  if (status === 413) return "payload_too_large";
  if (status === 404 || status === 405 || status === 415) return "protocol";
  if (status === 429 || status >= 500) return "transient";
  return "protocol";
}

async function ensureTelemetryTransportReady(
  deviceId?: string | null,
): Promise<Record<string, string>> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (deviceId && deviceId.trim()) {
    headers["X-Device-Id"] = deviceId.trim();
  }
  const auth = getAuthRuntimeSnapshot();

  if (auth.mode === "unknown") {
    throw new TelemetryApiError(0, "protocol", "Auth status desconocido");
  }
  if (auth.mode === "disabled") {
    return headers;
  }

  if (!auth.token) {
    throw new TelemetryApiError(401, "auth_required", "Autenticación requerida");
  }

  const expiration = parseExpiration(auth.expiresAtIso);
  if (!expiration.valid) {
    await handleSessionExpiredBeforeRequest();
    throw new TelemetryApiError(401, "auth_required", "Sesión vencida");
  }

  headers.Authorization = `Bearer ${auth.token}`;
  return headers;
}

async function postJson(
  path: string,
  body: unknown,
  deviceId?: string | null,
  timeoutMs = DEFAULT_TIMEOUT_MS,
): Promise<void> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const headers = await ensureTelemetryTransportReady(deviceId);
    const response = await fetch(`${getApiBaseUrl()}${path}`, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
      signal: controller.signal,
    });

    if (!response.ok) {
      const retryAfter = Number(response.headers.get("Retry-After") ?? "");
      const bodyText = sanitizeErrorText(await response.text());
      throw new TelemetryApiError(
        response.status,
        categorizeHttpStatus(response.status),
        bodyText || `HTTP ${response.status}`,
        Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
      );
    }
  } catch (error) {
    if (error instanceof TelemetryApiError) throw error;
    if (error instanceof Error && error.name === "AbortError") {
      throw new TelemetryApiError(408, "timeout", "Timeout al enviar telemetría");
    }
    throw new TelemetryApiError(0, "network", error instanceof Error ? error.message : "Error de red");
  } finally {
    clearTimeout(timeout);
  }
}

function assertDeviceId(deviceId: string): string {
  const normalized = deviceId.trim();
  if (!normalized) {
    throw new TelemetryApiError(0, "protocol", "deviceId vacío");
  }
  return normalized;
}

export async function sendSingleEvent(
  event: TelemetryEventPayload,
  deviceId: string,
): Promise<void> {
  await postJson(
    "/api/telemetry",
    { ...event, locationSource: event.locationSource ?? "gps" },
    assertDeviceId(deviceId),
  );
}

export async function sendBatchEvents(
  events: TelemetryEventPayload[],
  deviceId: string,
): Promise<void> {
  await postJson(
    "/api/telemetry/batch",
    {
      events: events.map((event) => ({ ...event, locationSource: event.locationSource ?? "gps" })),
    },
    assertDeviceId(deviceId),
  );
}
