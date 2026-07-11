import { TelemetryApiError } from "@/services/telemetry-api";

export function isPermanentSyncError(error: unknown): boolean {
  return error instanceof TelemetryApiError && [400, 401, 403, 404, 422].includes(error.status);
}

export function isTransientSyncError(error: unknown): boolean {
  if (error instanceof TelemetryApiError) {
    return error.status === 0 || error.status === 408 || error.status === 429 || error.status >= 500;
  }
  return true;
}

export function computeBackoffMs(retryCount: number, retryAfterSeconds?: number): number {
  if (retryAfterSeconds && retryAfterSeconds > 0) {
    return Math.min(retryAfterSeconds * 1000, 300_000);
  }
  const base = Math.min(2000 * 2 ** retryCount, 300_000);
  return base + Math.floor(Math.random() * base * 0.25);
}
