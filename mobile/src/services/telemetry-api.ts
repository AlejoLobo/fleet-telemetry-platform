// Cliente HTTP para enviar eventos de telemetría a la API
import { getApiBaseUrl } from "@/config/env";
import type { TelemetryEventPayload } from "@/types/telemetry";

// Error personalizado para fallos de la API
class TelemetryApiError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TelemetryApiError";
  }
}

// Realiza POST JSON y valida la respuesta
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

// Envía un solo evento de telemetría
export async function sendSingleEvent(event: TelemetryEventPayload): Promise<void> {
  await postJson("/api/telemetry", event);
}

// Envía varios eventos en un lote
export async function sendBatchEvents(events: TelemetryEventPayload[]): Promise<void> {
  await postJson("/api/telemetry/batch", { events });
}

export { TelemetryApiError };
