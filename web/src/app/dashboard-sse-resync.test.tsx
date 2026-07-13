/** @vitest-environment jsdom */
import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useFleetData } from "@/hooks/use-fleet-data";
import { useSseStream } from "@/hooks/use-sse-stream";
import { REALTIME_EVENTS } from "@/lib/realtime-events";
import * as sseClient from "@/lib/sse-fetch-client";

const fetchFleetLive = vi.fn();
const fetchAlertsLive = vi.fn();
const fetchOpsSummary = vi.fn();
const fetchTelemetrySnapshot = vi.fn();

vi.mock("@/lib/api-client", () => ({
  apiClient: {
    getSseUrl: () => "http://localhost:5000/api/events/stream",
    getAuthToken: vi.fn(() => "token"),
    fetchAuthStatus: vi.fn(async () => ({ enabled: true })),
    fetchFleetLive: (...args: unknown[]) => fetchFleetLive(...args),
    fetchAlertsLive: (...args: unknown[]) => fetchAlertsLive(...args),
    fetchOpsSummary: (...args: unknown[]) => fetchOpsSummary(...args),
  },
}));

vi.mock("@/lib/fleet-pagination", () => ({
  fetchTelemetrySnapshot: (...args: unknown[]) => fetchTelemetrySnapshot(...args),
}));

vi.mock("@/lib/utils", () => ({
  getApiBaseUrl: () => "http://localhost:5000",
}));

const vehicle = {
  vehicleId: "VH-001",
  name: "VH-001",
  status: "online",
  lastSeenAt: "2026-07-10T10:00:00Z",
  lastSpeedKmh: 1,
  lastLatitude: 1,
  lastLongitude: 1,
};

const vehiclePayload = {
  vehicleId: "VH-LIVE",
  name: "VH-LIVE",
  status: "online",
  lastSeenAt: "2026-07-10T10:00:00Z",
  lastSpeedKmh: 40,
  lastLatitude: 4.6,
  lastLongitude: -74.0,
};

function renderDashboardResyncHook() {
  const onFleetUpdate = vi.fn();
  const hook = renderHook(() => {
    const fleet = useFleetData("VH-001");
    useSseStream({
      enabled: fleet.dataSource === "api",
      authToken: "token",
      onFleetUpdate,
      onStreamReset: async () => {
        await fleet.refreshForResync("VH-001");
      },
    });
    return { fleet, onFleetUpdate };
  });
  return hook;
}

describe("dashboard SSE resync integration", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  beforeEach(() => {
    fetchFleetLive.mockReset();
    fetchAlertsLive.mockReset();
    fetchOpsSummary.mockReset();
    fetchTelemetrySnapshot.mockReset();
    fetchFleetLive.mockResolvedValue({
      vehicles: [vehicle],
      partial: false,
      truncated: false,
    });
    fetchAlertsLive.mockResolvedValue([]);
    fetchTelemetrySnapshot.mockResolvedValue({
      events: [{ eventId: "e1", vehicleId: "VH-001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
      partial: false,
      truncated: false,
    });
  });

  async function bootstrapApiMode(result: { current: { fleet: { fleetLoading: boolean; loadFromApi: () => Promise<void> } } }) {
    await waitFor(() => expect(result.current.fleet.fleetLoading).toBe(false));
    await result.current.fleet.loadFromApi();
  }

  it("Fallo_real_de_fetchFleetLive_mantiene_resyncRequired", async () => {
    let fleetAttempts = 0;
    fetchFleetLive.mockImplementation(async () => {
      fleetAttempts += 1;
      if (fleetAttempts === 1) {
        return { vehicles: [vehicle], partial: false, truncated: false };
      }
      throw new Error("fleet down");
    });

    const { result } = renderDashboardResyncHook();
    await bootstrapApiMode(result);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "50" }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "60",
        data: JSON.stringify(vehiclePayload),
      });
    });

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
    });

    expect(fleetAttempts).toBeGreaterThanOrEqual(2);
    expect(result.current.onFleetUpdate).not.toHaveBeenCalled();
  });

  it("Fallo_real_de_fetchAlertsLive_mantiene_resyncRequired", async () => {
    let alertsAttempts = 0;
    fetchAlertsLive.mockImplementation(async () => {
      alertsAttempts += 1;
      if (alertsAttempts === 1) return [];
      throw new Error("alerts down");
    });

    const { result } = renderDashboardResyncHook();
    await bootstrapApiMode(result);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "50" }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "61",
        data: JSON.stringify(vehiclePayload),
      });
    });

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
    });

    expect(alertsAttempts).toBeGreaterThanOrEqual(2);
    expect(result.current.onFleetUpdate).not.toHaveBeenCalled();
  });

  it("Fallo_real_de_telemetria_mantiene_resyncRequired", async () => {
    let telemetryAttempts = 0;
    fetchTelemetrySnapshot.mockImplementation(async () => {
      telemetryAttempts += 1;
      if (telemetryAttempts === 1) {
        return { events: [], partial: false, truncated: false };
      }
      return { events: [], partial: true, error: "telemetry down", truncated: false };
    });

    const { result } = renderDashboardResyncHook();
    await bootstrapApiMode(result);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "50" }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "62",
        data: JSON.stringify(vehiclePayload),
      });
    });

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
    });

    expect(telemetryAttempts).toBeGreaterThanOrEqual(2);
    expect(result.current.onFleetUpdate).not.toHaveBeenCalled();
  });

  it("Resync_real_exitoso_habilita_nuevamente_eventos_live", async () => {
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "70" }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "80",
        data: JSON.stringify(vehiclePayload),
      });
    });

    const { result } = renderDashboardResyncHook();
    await bootstrapApiMode(result);

    await waitFor(() => expect(result.current.onFleetUpdate).toHaveBeenCalled());
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("80");
  });
});
