import { getApiBaseUrl } from "@/config/env";
import type { TelemetryEventPayload } from "@/types/telemetry";

const DEFAULT_TIMEOUT_MS = 30_000;

export class TelemetryApiError extends Error {
  status: number;
  retryAfterSeconds?: number;

  constructor(status: number, message: string, retryAfterSeconds?: number) {
    super(message);
    this.name = "TelemetryApiError";
    this.status = status;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

async function postJson(path: string, body: unknown, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<void> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(`${getApiBaseUrl()}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: controller.signal,
    });

    if (!response.ok) {
      const retryAfter = Number(response.headers.get("Retry-After") ?? "");
      throw new TelemetryApiError(
        response.status,
        await response.text(),
        Number.isFinite(retryAfter) ? retryAfter : undefined,
      );
    }
  } catch (error) {
    if (error instanceof TelemetryApiError) throw error;
    if (error instanceof Error && error.name === "AbortError") {
      throw new TelemetryApiError(408, "Timeout al enviar telemetría");
    }
    throw new TelemetryApiError(0, error instanceof Error ? error.message : "Error de red");
  } finally {
    clearTimeout(timeout);
  }
}

export async function sendSingleEvent(event: TelemetryEventPayload): Promise<void> {
  await postJson("/api/telemetry", { ...event, locationSource: event.locationSource ?? "gps" });
}

export async function sendBatchEvents(events: TelemetryEventPayload[]): Promise<void> {
  await postJson("/api/telemetry/batch", {
    events: events.map((event) => ({ ...event, locationSource: event.locationSource ?? "gps" })),
  });
}
