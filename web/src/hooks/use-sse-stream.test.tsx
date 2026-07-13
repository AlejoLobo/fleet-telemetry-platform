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

  it("cancela la conexión al desmontarse mediante AbortController", async () => {
    const abortSignals: AbortSignal[] = [];
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, init) => {
      abortSignals.push(init.signal);
      return new Promise(() => {
        /* stream abierto hasta abort */
      });
    });

    const { unmount } = renderHook(() => useSseStream({ enabled: true }));
    await waitFor(() => expect(abortSignals).toHaveLength(1));

    unmount();
    expect(abortSignals[0]?.aborted).toBe(true);
    expect(consumeSpy).toHaveBeenCalled();
  });

  it("no reconecta indefinidamente después de 401", async () => {
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockRejectedValue(
      new sseClient.SseAuthError(401),
    );

    renderHook(() => useSseStream({ enabled: true, authToken: "bad-token" }));
    await waitFor(() => expect(consumeSpy).toHaveBeenCalledTimes(1));

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100));
    });

    expect(consumeSpy).toHaveBeenCalledTimes(1);
  });

  it("no reconecta indefinidamente después de 403", async () => {
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockRejectedValue(
      new sseClient.SseAuthError(403),
    );

    renderHook(() => useSseStream({ enabled: true, authToken: "forbidden-token" }));
    await waitFor(() => expect(consumeSpy).toHaveBeenCalledTimes(1));

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100));
    });

    expect(consumeSpy).toHaveBeenCalledTimes(1);
  });

  it("reconecta después de un error temporal de red", async () => {
    vi.spyOn(sseClient, "computeReconnectDelayMs").mockReturnValue(50);
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream")
      .mockRejectedValueOnce(new Error("network down"))
      .mockImplementation(async () => undefined);

    renderHook(() => useSseStream({ enabled: true }));
    await waitFor(() => expect(consumeSpy).toHaveBeenCalledTimes(1));

    await waitFor(
      () => expect(consumeSpy).toHaveBeenCalledTimes(2),
      { timeout: 2_000 },
    );
  });

  it("vuelve a conectar cuando cambia el token", async () => {
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, init) => {
      await new Promise<void>((resolve) => {
        init.signal.addEventListener("abort", () => resolve(), { once: true });
      });
    });

    const { rerender } = renderHook(
      ({ token }: { token: string | null }) => useSseStream({ enabled: true, authToken: token }),
      { initialProps: { token: "token-a" as string | null } },
    );

    await waitFor(() => expect(consumeSpy).toHaveBeenCalled());
    const callsBeforeRerender = consumeSpy.mock.calls.length;

    rerender({ token: "token-b" });
    await waitFor(() => expect(consumeSpy.mock.calls.length).toBeGreaterThan(callsBeforeRerender));
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

describe("useSseStream FT-005", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it("Reconexion_envia_Last_Event_ID", async () => {
    sessionStorage.setItem("fleet-sse-last-event-id", "55");
    const headers: Record<string, string>[] = [];
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, init) => {
      headers.push(init.headers ?? {});
      return undefined;
    });

    renderHook(() => useSseStream({ enabled: true }));
    await waitFor(() => expect(headers.length).toBeGreaterThan(0));
    expect(headers[0]?.["Last-Event-ID"]).toBe("55");
  });

  it("Evento_procesado_actualiza_ultimo_id", async () => {
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "88",
        data: JSON.stringify({
          vehicleId: "VH-ID",
          name: "VH-ID",
          status: "online",
          lastSeenAt: "2026-07-10T10:00:00Z",
          lastSpeedKmh: 40,
          lastLatitude: 4.6,
          lastLongitude: -74.0,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true }));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("88"));
  });

  it("Evento_invalido_no_avanza_ultimo_id", async () => {
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "90",
        data: "not-json",
      });
    });

    renderHook(() => useSseStream({ enabled: true }));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull();
  });

  it("Cambio_de_token_limpia_ultimo_id", async () => {
    sessionStorage.setItem("fleet-sse-last-event-id", "33");
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async () => undefined);

    const { rerender } = renderHook(
      ({ token }: { token: string | null }) => useSseStream({ enabled: true, authToken: token }),
      { initialProps: { token: "token-a" as string | null } },
    );

    rerender({ token: "token-b" });
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull());
  });

  it("Stream_reset_limpia_id_y_refresca_snapshot", async () => {
    sessionStorage.setItem("fleet-sse-last-event-id", "44");
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: 100 }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull();
  });

  it("Stream_reset_no_aplica_replay_incompleto", async () => {
    const onFleetUpdate = vi.fn();
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: 10 }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onFleetUpdate, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    expect(onFleetUpdate).not.toHaveBeenCalled();
  });

  it("Reconectar_a_otra_replica_con_gap_converge_por_snapshot", async () => {
    const onStreamReset = vi.fn(async () => {
      sessionStorage.removeItem("fleet-sse-last-event-id");
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "instance-restarted", latestEventId: 200 }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull();
  });

  it("Stream_reset_espera_refresh_antes_de_procesar_live", async () => {
    const steps: string[] = [];
    const onStreamReset = vi.fn(async () => {
      steps.push("refresh-start");
      await new Promise((resolve) => setTimeout(resolve, 50));
      steps.push("refresh-end");
    });
    const onFleetUpdate = vi.fn(() => steps.push("fleet"));

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: 10 }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "77",
        data: JSON.stringify({
          vehicleId: "VH-SEQ",
          name: "VH-SEQ",
          status: "online",
          lastSeenAt: "2026-07-10T10:00:00Z",
          lastSpeedKmh: 40,
          lastLatitude: 4.6,
          lastLongitude: -74.0,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset, onFleetUpdate }));
    await waitFor(() => expect(onFleetUpdate).toHaveBeenCalled());
    expect(steps).toEqual(["refresh-start", "refresh-end", "fleet"]);
  });

  it("Fallo_del_refresh_no_avanza_Last_Event_ID", async () => {
    let attempts = 0;
    const onStreamReset = vi.fn(async () => {
      attempts += 1;
      if (attempts < 2) throw new Error("refresh failed");
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: 10 }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "91",
        data: JSON.stringify({
          vehicleId: "VH-FAIL",
          name: "VH-FAIL",
          status: "online",
          lastSeenAt: "2026-07-10T10:00:00Z",
          lastSpeedKmh: 40,
          lastLatitude: 4.6,
          lastLongitude: -74.0,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(attempts).toBeGreaterThanOrEqual(2));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("91"));
  });

  it("Fallo_del_refresh_no_aplica_eventos_live", async () => {
    let attempts = 0;
    const onStreamReset = vi.fn(async () => {
      attempts += 1;
      if (attempts < 2) throw new Error("refresh failed");
    });
    const onFleetUpdate = vi.fn();

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: 10 }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "92",
        data: JSON.stringify({
          vehicleId: "VH-BLOCK",
          name: "VH-BLOCK",
          status: "online",
          lastSeenAt: "2026-07-10T10:00:00Z",
          lastSpeedKmh: 40,
          lastLatitude: 4.6,
          lastLongitude: -74.0,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset, onFleetUpdate }));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
    });
    expect(attempts).toBeGreaterThanOrEqual(2);
    expect(onFleetUpdate).toHaveBeenCalledTimes(1);
  });

  it("Resync_fallido_se_reintenta_hasta_completar", async () => {
    let attempts = 0;
    const onStreamReset = vi.fn(async () => {
      attempts += 1;
      if (attempts < 3) throw new Error("still failing");
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: 10 }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(attempts).toBe(3));
  });

  it("Solo_despues_del_snapshot_se_reanuda_el_cursor", async () => {
    let refreshCompleted = false;
    const onStreamReset = vi.fn(async () => {
      await new Promise((resolve) => setTimeout(resolve, 30));
      refreshCompleted = true;
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: 10 }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "93",
        data: JSON.stringify({
          vehicleId: "VH-CURSOR",
          name: "VH-CURSOR",
          status: "online",
          lastSeenAt: "2026-07-10T10:00:00Z",
          lastSpeedKmh: 40,
          lastLatitude: 4.6,
          lastLongitude: -74.0,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("93"));
    expect(refreshCompleted).toBe(true);
  });
});
