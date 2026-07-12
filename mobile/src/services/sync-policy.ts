import { TelemetryApiError, type TelemetryApiErrorCategory } from "@/services/telemetry-api";

export type SyncErrorAction =
  | "mark_synced"
  | "isolate_validation"
  | "stop_auth_required"
  | "stop_forbidden"
  | "stop_transient"
  | "stop_configuration"
  | "stop_unexpected"
  | "split_payload";

export type SyncErrorClassification = {
  action: SyncErrorAction;
  category: TelemetryApiErrorCategory;
  status: number;
  retryAfterSeconds?: number;
};

function readStatus(error: unknown): number | null {
  if (error instanceof TelemetryApiError) return error.status;
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { status?: unknown }).status;
    return typeof status === "number" ? status : null;
  }
  return null;
}

function readCategory(error: unknown): TelemetryApiErrorCategory {
  if (error instanceof TelemetryApiError) return error.category;
  const status = readStatus(error);
  if (status === 401) return "auth_required";
  if (status === 403) return "forbidden";
  if (status === 408) return "timeout";
  if (status === 0) return "network";
  if (status === 400 || status === 422) return "validation";
  if (status === 413) return "payload_too_large";
  if (status === 404 || status === 405 || status === 415) return "protocol";
  if (status === 429 || (status !== null && status >= 500)) return "transient";
  return "protocol";
}

export function classifySyncError(error: unknown): SyncErrorClassification {
  if (!(error instanceof TelemetryApiError)) {
    return { action: "stop_unexpected", category: "protocol", status: 0 };
  }

  const status = error.status;
  const category = error.category;
  const retryAfterSeconds = error.retryAfterSeconds;

  if (category === "validation") {
    return { action: "isolate_validation", category, status, retryAfterSeconds };
  }
  if (category === "auth_required") {
    return { action: "stop_auth_required", category, status, retryAfterSeconds };
  }
  if (category === "forbidden") {
    return { action: "stop_forbidden", category, status, retryAfterSeconds };
  }
  if (category === "payload_too_large") {
    return { action: "split_payload", category, status, retryAfterSeconds };
  }
  if (category === "protocol") {
    return { action: "stop_configuration", category, status, retryAfterSeconds };
  }
  if (category === "transient" || category === "timeout" || category === "network") {
    return { action: "stop_transient", category, status, retryAfterSeconds };
  }
  return { action: "stop_unexpected", category, status, retryAfterSeconds };
}

export function isValidationSyncError(error: unknown): boolean {
  return readCategory(error) === "validation";
}

export function isPermanentDataError(error: unknown): boolean {
  const category = readCategory(error);
  return category === "validation" || category === "payload_too_large";
}

export function isTransientSyncError(error: unknown): boolean {
  const category = readCategory(error);
  return category === "transient" || category === "timeout" || category === "network";
}

export function isAuthSyncError(error: unknown): boolean {
  return readCategory(error) === "auth_required";
}

export function isForbiddenSyncError(error: unknown): boolean {
  return readCategory(error) === "forbidden";
}

export function isConfigurationSyncError(error: unknown): boolean {
  return readCategory(error) === "protocol";
}

export function getRetryAfterSeconds(error: unknown): number | undefined {
  if (error instanceof TelemetryApiError) return error.retryAfterSeconds;
  return undefined;
}

export function computeBackoffMs(retryCount: number, retryAfterSeconds?: number): number {
  if (retryAfterSeconds && retryAfterSeconds > 0) {
    return Math.min(retryAfterSeconds * 1000, 300_000);
  }
  const base = Math.min(2000 * 2 ** retryCount, 300_000);
  return base + Math.floor(Math.random() * base * 0.25);
}

// Compatibilidad con pruebas existentes: solo datos inválidos son permanentes del evento.
export function isPermanentSyncError(error: unknown): boolean {
  return isPermanentDataError(error);
}
