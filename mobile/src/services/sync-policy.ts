import { TelemetryApiError } from "@/services/telemetry-api";

function readStatus(error: unknown): number | null {
  if (error instanceof TelemetryApiError) return error.status;
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { status?: unknown }).status;
    return typeof status === "number" ? status : null;
  }
  return null;
}

function readRetryAfter(error: unknown): number | undefined {
  if (error instanceof TelemetryApiError) return error.retryAfterSeconds;
  if (typeof error === "object" && error !== null && "retryAfterSeconds" in error) {
    const retryAfter = (error as { retryAfterSeconds?: unknown }).retryAfterSeconds;
    return typeof retryAfter === "number" ? retryAfter : undefined;
  }
  return undefined;
}

export function isPermanentSyncError(error: unknown): boolean {
  const status = readStatus(error);
  return status !== null && [400, 401, 403, 404, 422].includes(status);
}

export function isTransientSyncError(error: unknown): boolean {
  const status = readStatus(error);
  if (status !== null) {
    return status === 0 || status === 408 || status === 429 || status >= 500;
  }
  return true;
}

export function getRetryAfterSeconds(error: unknown): number | undefined {
  return readRetryAfter(error);
}

export function computeBackoffMs(retryCount: number, retryAfterSeconds?: number): number {
  if (retryAfterSeconds && retryAfterSeconds > 0) {
    return Math.min(retryAfterSeconds * 1000, 300_000);
  }
  const base = Math.min(2000 * 2 ** retryCount, 300_000);
  return base + Math.floor(Math.random() * base * 0.25);
}
