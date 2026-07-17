/** @vitest-environment jsdom */
import { cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useFleetData } from "@/hooks/use-fleet-data";
import { useSseStream } from "@/hooks/use-sse-stream";
import { REALTIME_EVENTS } from "@/lib/realtime-events";
import * as sseClient from "@/lib/sse-fetch-client";
import type { SseParsedEvent } from "@/lib/sse-fetch-client";

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
  deviceId: "00000000-0000-4000-8000-000000000001",
  vehicleName: "00000000-0000-4000-8000-000000000001",
  vehicleType: "car" as const,
  status: "online",
  lastSeenAt: "2026-07-10T10:00:00Z",
  lastSpeedKmh: 1,
  lastLatitude: 1,
  lastLongitude: 1,
};

const vehiclePayload = {
  deviceId: "00000000-0000-4000-8000-000000000095",
  vehicleName: "00000000-0000-4000-8000-000000000095",
  vehicleType: "car" as const,
  status: "online",
  lastSeenAt: "2026-07-10T10:00:00Z",
  lastSpeedKmh: 40,
  lastLatitude: 4.6,
  lastLongitude: -74.0,
};

type StreamHandlers = {
  onEvent: (event: SseParsedEvent) => void | Promise<void>;
};

function renderDashboardResyncHook() {
  const onFleetUpdate = vi.fn();
  const hook = renderHook(() => {
    const fleet = useFleetData("00000000-0000-4000-8000-000000000001");
    useSseStream({
      enabled: fleet.dataSource === "api",
      authToken: "token",
      onFleetUpdate,
      onStreamReset: async () => {
        await fleet.refreshForResync("00000000-0000-4000-8000-000000000001");
      },
    });
    return { fleet, onFleetUpdate };
  });
  return hook;
}

describe("dashboard SSE resync integration", () => {
  afterEach(() => {
    // Desmontar antes de restaurar mocks: evita que un resync en vuelo
    // complete con el mock “sano” del siguiente test y escriba sessionStorage.
    cleanup();
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
      events: [{ eventId: "e1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
      partial: false,
      truncated: false,
    });
  });

  async function bootstrapApiMode(result: { current: { fleet: { fleetLoading: boolean; loadFromApi: () => Promise<void> } } }) {
    await waitFor(() => expect(result.current.fleet.fleetLoading).toBe(false));
    await result.current.fleet.loadFromApi();
  }

  async function openControlledStream(): Promise<StreamHandlers> {
    let resolveHandlers: ((handlers: StreamHandlers) => void) | undefined;
    const handlersReady = new Promise<StreamHandlers>((resolve) => {
      resolveHandlers = resolve;
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      resolveHandlers?.(handlers);
      return new Promise(() => {
        /* stream controlado por el test */
      });
    });

    return handlersReady;
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

    const handlersPromise = openControlledStream();
    const { result, unmount } = renderDashboardResyncHook();
    await bootstrapApiMode(result);
    const handlers = await handlersPromise;

    void handlers.onEvent({
      event: REALTIME_EVENTS.streamReset,
      data: JSON.stringify({ reason: "replay-gap", latestEventId: "50" }),
    });

    await waitFor(() => expect(fleetAttempts).toBeGreaterThanOrEqual(2));
    void handlers.onEvent({
      event: REALTIME_EVENTS.vehicleUpdate,
      id: "60",
      data: JSON.stringify(vehiclePayload),
    });
    await waitFor(() => expect(fleetAttempts).toBeGreaterThanOrEqual(2));
    expect(result.current.onFleetUpdate).not.toHaveBeenCalled();
    unmount();
  });

  it("Fallo_real_de_fetchAlertsLive_mantiene_resyncRequired", async () => {
    let alertsAttempts = 0;
    fetchAlertsLive.mockImplementation(async () => {
      alertsAttempts += 1;
      if (alertsAttempts === 1) return [];
      throw new Error("alerts down");
    });

    const handlersPromise = openControlledStream();
    const { result, unmount } = renderDashboardResyncHook();
    await bootstrapApiMode(result);
    const handlers = await handlersPromise;

    void handlers.onEvent({
      event: REALTIME_EVENTS.streamReset,
      data: JSON.stringify({ reason: "replay-gap", latestEventId: "50" }),
    });

    await waitFor(() => expect(alertsAttempts).toBeGreaterThanOrEqual(2));
    void handlers.onEvent({
      event: REALTIME_EVENTS.vehicleUpdate,
      id: "61",
      data: JSON.stringify(vehiclePayload),
    });
    await waitFor(() => expect(alertsAttempts).toBeGreaterThanOrEqual(2));
    expect(result.current.onFleetUpdate).not.toHaveBeenCalled();
    unmount();
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

    const handlersPromise = openControlledStream();
    const { result, unmount } = renderDashboardResyncHook();
    await bootstrapApiMode(result);
    const handlers = await handlersPromise;

    void handlers.onEvent({
      event: REALTIME_EVENTS.streamReset,
      data: JSON.stringify({ reason: "replay-gap", latestEventId: "50" }),
    });

    await waitFor(() => expect(telemetryAttempts).toBeGreaterThanOrEqual(2));
    void handlers.onEvent({
      event: REALTIME_EVENTS.vehicleUpdate,
      id: "62",
      data: JSON.stringify(vehiclePayload),
    });
    await waitFor(() => expect(telemetryAttempts).toBeGreaterThanOrEqual(2));
    expect(result.current.onFleetUpdate).not.toHaveBeenCalled();
    unmount();
  });

  it("Resync_real_exitoso_habilita_nuevamente_eventos_live", async () => {
    const handlersPromise = openControlledStream();
    const { result, unmount } = renderDashboardResyncHook();
    await bootstrapApiMode(result);
    const handlers = await handlersPromise;

    await handlers.onEvent({
      event: REALTIME_EVENTS.streamReset,
      data: JSON.stringify({ reason: "replay-gap", latestEventId: "70" }),
    });
    await handlers.onEvent({
      event: REALTIME_EVENTS.vehicleUpdate,
      id: "80",
      data: JSON.stringify(vehiclePayload),
    });

    await waitFor(() => expect(result.current.onFleetUpdate).toHaveBeenCalled());
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("80"));
    unmount();
  });
});
