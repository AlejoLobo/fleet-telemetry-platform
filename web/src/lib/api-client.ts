import type { AiQueryResponse, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { getApiBaseUrl, isMockMode } from "@/lib/utils";
import {
  generateMockAiResponse,
  getMockDataset,
  getMockTelemetry,
  refreshMockDataset,
} from "@/mocks/fleet-data";

type ApiVehicle = VehicleStatus & { lastHeadingDegrees?: number | null };

function normalizeVehicle(vehicle: ApiVehicle): VehicleStatus {
  return {
    ...vehicle,
    headingDegrees: vehicle.headingDegrees ?? vehicle.lastHeadingDegrees ?? null,
  };
}

class ApiError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ApiError";
  }
}

function authHeaders(): Record<string, string> {
  if (typeof window === "undefined") return {};
  const token = localStorage.getItem("fleet_api_token");
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function fetchJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...authHeaders(),
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
    if (isMockMode()) return getMockDataset().vehicles;
    const data = await fetchJson<ApiVehicle[]>("/api/fleet");
    return data.map(normalizeVehicle);
  },

  async getAlerts(): Promise<FleetAlert[]> {
    if (isMockMode()) return getMockDataset().alerts;
    return fetchJson<FleetAlert[]>("/api/alerts");
  },

  async getTelemetry(vehicleId: string): Promise<TelemetryEvent[]> {
    if (isMockMode()) return getMockTelemetry(vehicleId);
    const to = new Date().toISOString();
    const from = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    return fetchJson<TelemetryEvent[]>(
      `/api/telemetry/${encodeURIComponent(vehicleId)}?from=${from}&to=${to}`,
    );
  },

  async queryAi(question: string): Promise<AiQueryResponse> {
    if (isMockMode()) return generateMockAiResponse();
    return fetchJson<AiQueryResponse>("/api/ai/query", {
      method: "POST",
      body: JSON.stringify({ question }),
    });
  },

  getSseUrl(): string {
    return `${getApiBaseUrl()}/api/events/stream`;
  },

  /** Regenera datos de demostración (mínimo 6 vehículos por defecto) */
  refreshMockData(vehicleCount = 6) {
    refreshMockDataset(vehicleCount);
  },

  async fetchFleetLive(): Promise<VehicleStatus[]> {
    const data = await fetchJson<ApiVehicle[]>("/api/fleet?liveOnly=true");
    return data.map(normalizeVehicle);
  },

  async fetchAlertsLive(): Promise<FleetAlert[]> {
    return fetchJson<FleetAlert[]>("/api/alerts");
  },

  async fetchTelemetryLive(vehicleId: string): Promise<TelemetryEvent[]> {
    const to = new Date().toISOString();
    const from = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    return fetchJson<TelemetryEvent[]>(
      `/api/telemetry/${encodeURIComponent(vehicleId)}?from=${from}&to=${to}`,
    );
  },

  async acknowledgeAlert(alertId: string): Promise<void> {
    await fetchJson(`/api/alerts/${encodeURIComponent(alertId)}/acknowledge`, {
      method: "PATCH",
    });
  },

  setAuthToken(token: string | null) {
    if (typeof window === "undefined") return;
    if (token) localStorage.setItem("fleet_api_token", token);
    else localStorage.removeItem("fleet_api_token");
  },
};

export { ApiError };
