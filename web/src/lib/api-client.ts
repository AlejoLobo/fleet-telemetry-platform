import type { AiQueryResponse, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { getApiBaseUrl } from "@/lib/utils";

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

/** Cliente HTTP del backend .NET. El modo demo usa mocks en hooks, no aquí. */
export const apiClient = {
  getSseUrl(): string {
    return `${getApiBaseUrl()}/api/events/stream`;
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

  async queryAi(question: string): Promise<AiQueryResponse> {
    return fetchJson<AiQueryResponse>("/api/ai/query", {
      method: "POST",
      body: JSON.stringify({ question }),
    });
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
