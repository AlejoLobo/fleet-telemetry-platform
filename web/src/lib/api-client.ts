import type { AiQueryResponse, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { getApiBaseUrl } from "@/lib/utils";
import { normalizeVehicles } from "@/lib/fleet-normalize";

type LoginResponse = {
  token: string;
  expiresInMinutes: number;
};

type AuthStatusResponse = {
  enabled: boolean;
};

export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
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
    let detail = `Error ${response.status} en ${path}`;
    try {
      const body = (await response.json()) as { error?: string };
      if (body.error) detail = body.error;
    } catch {
      // respuesta no JSON
    }
    throw new ApiError(detail, response.status);
  }

  return response.json() as Promise<T>;
}

/** Cliente HTTP del backend .NET. El modo demo usa mocks en hooks, no aquí. */
export const apiClient = {
  getSseUrl(): string {
    return `${getApiBaseUrl()}/api/events/stream`;
  },

  async fetchAuthStatus(): Promise<AuthStatusResponse> {
    return fetchJson<AuthStatusResponse>("/api/auth/status");
  },

  async login(username: string, password: string): Promise<void> {
    const response = await fetchJson<LoginResponse>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    });
    apiClient.setAuthToken(response.token);
  },

  async fetchFleetLive(): Promise<VehicleStatus[]> {
    // Todos los vehículos con última telemetría (no solo "online" últimos 5 min).
    // liveOnly=true dejaba mapa/flota vacíos si los eventos eran más antiguos.
    const data = await fetchJson<VehicleStatus[]>("/api/fleet");
    return normalizeVehicles(data);
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

  hasAuthToken(): boolean {
    if (typeof window === "undefined") return false;
    return Boolean(localStorage.getItem("fleet_api_token"));
  },
};
