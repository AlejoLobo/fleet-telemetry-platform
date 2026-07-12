/** @vitest-environment jsdom */
import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useSseStream } from "@/hooks/use-sse-stream";
import { mergeVehicleUpdates } from "@/lib/fleet-merge";
import { REALTIME_EVENTS } from "@/lib/realtime-events";
import * as sseClient from "@/lib/sse-fetch-client";
import type { VehicleStatus } from "@/types/fleet";

vi.mock("@/lib/api-client", () => ({
  apiClient: {
    getSseUrl: () => "http://localhost:5000/api/events/stream",
    getAuthToken: vi.fn(() => null),
    fetchAuthStatus: vi.fn(async () => ({ enabled: false })),
  },
}));

describe("useSseStream FT-001", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it("KafkaPush_vehicle_update_llega_al_cliente_web", async () => {
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        data: JSON.stringify({
          vehicleId: "VH-KAFKA",
          name: "VH-KAFKA",
          status: "online",
          lastSeenAt: "2026-07-10T10:05:00Z",
          lastSpeedKmh: 88,
          lastLatitude: 4.7,
          lastLongitude: -74.05,
        }),
      });
    });

    const onFleetUpdate = vi.fn();
    renderHook(() => useSseStream({ enabled: true, onFleetUpdate }));

    await waitFor(() => expect(onFleetUpdate).toHaveBeenCalledTimes(1));
    expect(onFleetUpdate.mock.calls[0]?.[0]).toEqual([
      expect.objectContaining({ vehicleId: "VH-KAFKA", lastSpeedKmh: 88 }),
    ]);
  });

  it("Polling_fleet_update_array_sigue_soportado", async () => {
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.fleetUpdate,
        data: JSON.stringify([
          { vehicleId: "VH-001", status: "online", lastSeenAt: "2026-07-10T10:00:00Z" },
          { vehicleId: "VH-002", status: "offline", lastSeenAt: "2026-07-10T09:00:00Z" },
        ]),
      });
    });

    const onFleetUpdate = vi.fn();
    renderHook(() => useSseStream({ enabled: true, onFleetUpdate }));

    await waitFor(() => expect(onFleetUpdate).toHaveBeenCalled());
    expect(onFleetUpdate.mock.calls[0]?.[0]).toHaveLength(2);
  });

  it("SSE_merge_real_no_reemplaza_snapshot_completo", async () => {
    const baseFleet: VehicleStatus[] = [
      { vehicleId: "VH-001", name: "VH-001", status: "online", lastSeenAt: "2026-07-10T09:00:00Z", lastSpeedKmh: 10, lastLatitude: 1, lastLongitude: 1 },
      { vehicleId: "VH-002", name: "VH-002", status: "online", lastSeenAt: "2026-07-10T09:00:00Z", lastSpeedKmh: 20, lastLatitude: 2, lastLongitude: 2 },
    ];

    let patches: VehicleStatus[] = [];
    const onFleetUpdate = (updates: VehicleStatus[]) => {
      patches = mergeVehicleUpdates(patches, updates);
    };

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        data: JSON.stringify({
          vehicleId: "VH-001",
          name: "VH-001",
          status: "offline",
          lastSeenAt: "2026-07-10T10:05:00Z",
          lastSpeedKmh: 0,
          lastLatitude: 1.1,
          lastLongitude: 1.1,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onFleetUpdate }));

    await waitFor(() => expect(patches).toHaveLength(1));

    const displayVehicles = mergeVehicleUpdates(baseFleet, patches);
    expect(displayVehicles).toHaveLength(2);
    expect(displayVehicles.find((v) => v.vehicleId === "VH-001")?.status).toBe("offline");
    expect(displayVehicles.find((v) => v.vehicleId === "VH-002")?.status).toBe("online");
  });

  it("procesa fleet-update y alert desde eventos SSE", async () => {
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.fleetUpdate,
        data: JSON.stringify([{ vehicleId: "VH-001", status: "online" }]),
      });
      handlers.onEvent({
        event: REALTIME_EVENTS.alert,
        data: JSON.stringify({
          alertId: "a1",
          vehicleId: "VH-001",
          alertType: "overspeed",
          severity: "critical",
          message: "x",
          createdAt: "2026-07-11T00:00:00Z",
          isAcknowledged: false,
        }),
      });
    });

    const onFleetUpdate = vi.fn();
    const onAlert = vi.fn();
    renderHook(() => useSseStream({ enabled: true, onFleetUpdate, onAlert }));

    await waitFor(() => {
      expect(onFleetUpdate).toHaveBeenCalled();
      expect(onAlert).toHaveBeenCalled();
    });
  });
});
