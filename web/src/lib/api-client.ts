import type { AiQueryResponse, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { getApiBaseUrl, isMockMode } from "@/lib/utils";
import { mockAiResponse, mockAlerts, mockTelemetry, mockVehicles } from "@/mocks/fleet-data";

class ApiError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ApiError";
  }
}

async function fetchJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    throw new ApiError(`Error ${response.status} en ${path}`);
  }

  return response.json() as Promise<T>;
}

export const apiClient = {
  async getFleet(): Promise<VehicleStatus[]> {
    if (isMockMode()) return mockVehicles;
    return fetchJson<VehicleStatus[]>("/api/fleet");
  },

  async getAlerts(): Promise<FleetAlert[]> {
    if (isMockMode()) return mockAlerts;
    return fetchJson<FleetAlert[]>("/api/alerts");
  },

  async getTelemetry(vehicleId: string): Promise<TelemetryEvent[]> {
    if (isMockMode()) return mockTelemetry.filter((e) => e.vehicleId === vehicleId);
    const to = new Date().toISOString();
    const from = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    return fetchJson<TelemetryEvent[]>(
      `/api/telemetry/${encodeURIComponent(vehicleId)}?from=${from}&to=${to}`,
    );
  },

  async queryAi(question: string): Promise<AiQueryResponse> {
    if (isMockMode()) return mockAiResponse;
    return fetchJson<AiQueryResponse>("/api/ai/query", {
      method: "POST",
      body: JSON.stringify({ question }),
    });
  },

  getSseUrl(): string {
    return `${getApiBaseUrl()}/api/events/stream`;
  },
};

export { ApiError };
