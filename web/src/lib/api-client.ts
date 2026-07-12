/** Cliente HTTP para comunicarse con el backend .NET. */
import type { AiQueryResponse, FleetAlert, TelemetryEvent, VehicleStatus } from "@/types/fleet";
import { getApiBaseUrl } from "@/lib/utils";
import {
  fetchFleetSnapshot,
  fetchTelemetrySnapshot,
  type FleetSnapshotResult,
  type TelemetrySnapshotResult,
} from "@/lib/fleet-pagination";

type LoginResponse = {
  token: string;
  expiresInMinutes: number;
};

type AuthStatusResponse = {
  enabled: boolean;
};

/** Error HTTP con código de estado. */
export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

/** Obtiene el token JWT del almacenamiento local. */
function authHeaders(): Record<string, string> {
  if (typeof window === "undefined") return {};
  const token = localStorage.getItem("fleet_api_token");
  return token ? { Authorization: `Bearer ${token}` } : {};
}

/** Realiza petición GET/POST y parsea JSON. */
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

  getAuthToken(): string | null {
    if (typeof window === "undefined") return null;
    return localStorage.getItem("fleet_api_token");
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

  async fetchFleetLive(options?: {
    maxVehicles?: number;
    pageSize?: number;
    liveOnly?: boolean;
    excludeSimulated?: boolean;
    signal?: AbortSignal;
  }): Promise<FleetSnapshotResult> {
    return fetchFleetSnapshot(options);
  },
  async fetchAlertsLive(): Promise<FleetAlert[]> {
    return fetchJson<FleetAlert[]>("/api/alerts");
  },

  async fetchOpsSummary(): Promise<{
    totalVehicles: number;
    activeVehicles: number;
    criticalAlerts: number;
  }> {
    const summary = await fetchJson<{
      totalVehicles: number;
      activeVehicles: number;
      criticalAlerts: number;
    }>("/api/ops/summary");
    return summary;
  },

  async fetchTelemetryLive(vehicleId: string): Promise<TelemetrySnapshotResult> {
    return fetchTelemetrySnapshot(vehicleId);
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
    return Boolean(apiClient.getAuthToken());
  },
};
