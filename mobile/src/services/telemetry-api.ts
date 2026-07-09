import { getApiBaseUrl } from "@/config/env";
import type { TelemetryEventPayload } from "@/types/telemetry";

class TelemetryApiError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TelemetryApiError";
  }
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new TelemetryApiError(`Error ${response.status}: ${text || path}`);
  }

  return response.json() as Promise<T>;
}

export async function sendSingleEvent(event: TelemetryEventPayload): Promise<void> {
  await postJson("/api/telemetry", event);
}

export async function sendBatchEvents(events: TelemetryEventPayload[]): Promise<void> {
  await postJson("/api/telemetry/batch", { events });
}

export { TelemetryApiError };
