import { getApiBaseUrl } from "@/config/env";
import type { TelemetryEventPayload } from "@/types/telemetry";

export class TelemetryApiError extends Error {
  status: number;
  retryAfterSeconds?: number;
  constructor(status: number, message: string, retryAfterSeconds?: number) {
    super(message); this.name = "TelemetryApiError"; this.status = status; this.retryAfterSeconds = retryAfterSeconds;
  }
}

async function postJson(path: string, body: unknown): Promise<void> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  if (!response.ok) {
    const retryAfter = Number(response.headers.get("Retry-After") ?? "");
    throw new TelemetryApiError(response.status, await response.text(), Number.isFinite(retryAfter) ? retryAfter : undefined);
  }
}

export async function sendSingleEvent(event: TelemetryEventPayload): Promise<void> {
  await postJson("/api/telemetry", { ...event, locationSource: event.locationSource ?? "gps" });
}

export async function sendBatchEvents(events: TelemetryEventPayload[]): Promise<void> {
  await postJson("/api/telemetry/batch", { events: events.map((e) => ({ ...e, locationSource: e.locationSource ?? "gps" })) });
}
