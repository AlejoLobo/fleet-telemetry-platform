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

  it("Cambio_de_token_limpia_cursor_y_marca_de_resync", async () => {
    sessionStorage.setItem("fleet-sse-last-event-id", "33");
    sessionStorage.setItem("fleet-sse-resync-pending", "1");
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async () => undefined);

    const { rerender } = renderHook(
      ({ token }: { token: string | null }) => useSseStream({ enabled: true, authToken: token }),
      { initialProps: { token: "token-a" as string | null } },
    );

    rerender({ token: "token-b" });
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull());
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBeNull();
  });

  it("Stream_reset_exitoso_guarda_latestEventId_como_cursor_base", async () => {
    sessionStorage.setItem("fleet-sse-last-event-id", "44");
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "100" }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "101",
        data: JSON.stringify({
          vehicleId: "VH-CUTOVER",
          name: "VH-CUTOVER",
          status: "online",
          lastSeenAt: "2026-07-10T10:00:00Z",
          lastSpeedKmh: 40,
          lastLatitude: 4.6,
          lastLongitude: -74.0,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("101"));
  });

  it("Resync_fallido_no_guarda_latestEventId", async () => {
    let attempts = 0;
    const onStreamReset = vi.fn(async () => {
      attempts += 1;
      if (attempts < 2) throw new Error("refresh failed");
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "100" }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100));
    });
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull();

    await waitFor(() => expect(attempts).toBeGreaterThanOrEqual(2));
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("100");
  });

  it("Desmonte_durante_snapshot_no_permite_escritura_del_resync_antiguo", async () => {
    let resolveOldSnapshot: (() => void) | undefined;
    const onStreamResetOld = vi.fn(async () => {
      await new Promise<void>((resolve) => {
        resolveOldSnapshot = resolve;
      });
    });
    const onFleetUpdate = vi.fn();
    let connection = 0;
    const vehiclePayload = {
      vehicleId: "VH-UNMOUNT",
      name: "VH-UNMOUNT",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 40,
      lastLatitude: 4.6,
      lastLongitude: -74.0,
    };

    vi.spyOn(sseClient, "computeReconnectDelayMs").mockReturnValue(10);
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      connection += 1;
      if (connection === 1) {
        await handlers.onEvent({
          event: REALTIME_EVENTS.streamReset,
          data: JSON.stringify({ reason: "replay-gap", latestEventId: "88" }),
        });
        return new Promise(() => {
          /* abierta hasta desmontar */
        });
      }

      await handlers.onEvent({ event: REALTIME_EVENTS.connected, data: "{}" });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "99",
        data: JSON.stringify(vehiclePayload),
      });
      return new Promise(() => {
        /* mantener abierta */
      });
    });

    const first = renderHook(() => useSseStream({
      enabled: true,
      onStreamReset: onStreamResetOld,
      onFleetUpdate,
    }));
    await waitFor(() => expect(onStreamResetOld).toHaveBeenCalledTimes(1));
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBe("1");
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull();

    first.unmount();
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBe("1");

    const onStreamResetNew = vi.fn(async () => undefined);
    renderHook(() => useSseStream({
      enabled: true,
      onStreamReset: onStreamResetNew,
      onFleetUpdate,
    }));
    await waitFor(() => expect(onStreamResetNew).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("99"));
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBeNull();

    resolveOldSnapshot?.();
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("99");
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBeNull();
  });

  it("Cambio_de_token_durante_snapshot_impide_escritura_del_usuario_anterior", async () => {
    let resolveOldSnapshot: (() => void) | undefined;
    const onStreamResetOld = vi.fn(async () => {
      await new Promise<void>((resolve) => {
        resolveOldSnapshot = resolve;
      });
    });
    let connection = 0;

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      connection += 1;
      if (connection === 1) {
        await handlers.onEvent({
          event: REALTIME_EVENTS.streamReset,
          data: JSON.stringify({ reason: "replay-gap", latestEventId: "77" }),
        });
        return new Promise(() => {
          /* abierta hasta cambio de token */
        });
      }

      await handlers.onEvent({ event: REALTIME_EVENTS.connected, data: "{}" });
      return new Promise(() => {
        /* sesión nueva sin cutover del usuario anterior */
      });
    });

    const { rerender, unmount } = renderHook(
      ({ token }: { token: string | null }) => useSseStream({
        enabled: true,
        authToken: token,
        onStreamReset: onStreamResetOld,
      }),
      { initialProps: { token: "token-old" as string | null } },
    );

    await waitFor(() => expect(onStreamResetOld).toHaveBeenCalledTimes(1));
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBe("1");

    rerender({ token: "token-new" });
    await waitFor(() => expect(connection).toBeGreaterThanOrEqual(2));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull());
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBeNull();

    sessionStorage.setItem("fleet-sse-last-event-id", "120");
    sessionStorage.setItem("fleet-sse-resync-pending", "1");

    resolveOldSnapshot?.();
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("120");
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBe("1");
    unmount();
  });

  it("Stream_reset_con_cutover_valido_activa_marca_antes_del_snapshot", async () => {
    let sawMarkerDuringSnapshot = false;
    const onStreamReset = vi.fn(async () => {
      sawMarkerDuringSnapshot = sessionStorage.getItem("fleet-sse-resync-pending") === "1";
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "42" }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    expect(sawMarkerDuringSnapshot).toBe(true);
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("42"));
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBeNull();
  });

  it("Segunda_conexion_sin_stream_reset_ejecuta_snapshot_por_marca_persistida", async () => {
    const onStreamReset = vi.fn(async () => undefined);
    const onFleetUpdate = vi.fn();
    let connection = 0;
    const vehiclePayload = {
      vehicleId: "VH-RECONNECT",
      name: "VH-RECONNECT",
      status: "online",
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 40,
      lastLatitude: 4.6,
      lastLongitude: -74.0,
    };

    vi.spyOn(sseClient, "computeReconnectDelayMs").mockReturnValue(10);
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      connection += 1;
      if (connection === 1) {
        await handlers.onEvent({ event: REALTIME_EVENTS.connected, data: "{}" });
        await handlers.onEvent({
          event: REALTIME_EVENTS.streamReset,
          data: JSON.stringify({ reason: "instance-restarted", latestEventId: null }),
        });
        throw new Error("disconnect after first snapshot");
      }

      if (connection === 2) {
        await handlers.onEvent({ event: REALTIME_EVENTS.connected, data: "{}" });
        await handlers.onEvent({
          event: REALTIME_EVENTS.vehicleUpdate,
          id: "55",
          data: JSON.stringify(vehiclePayload),
        });
        return new Promise(() => {
          /* mantener conexión abierta para evitar reconexiones en bucle */
        });
      }

      return undefined;
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset, onFleetUpdate }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(onFleetUpdate).toHaveBeenCalledTimes(1));
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBeNull();
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("55");
  });

  it("Resync_fallido_con_latestEventId_null_conserva_marca", async () => {
    let attempts = 0;
    const onStreamReset = vi.fn(async () => {
      attempts += 1;
      if (attempts < 2) throw new Error("snapshot failed");
    });

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "instance-restarted", latestEventId: null }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(attempts).toBeGreaterThanOrEqual(2));
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBe("1");
  });

  it("LatestEventId_null_mantiene_marca_hasta_primer_evento_con_id", async () => {
    const onStreamReset = vi.fn(async () => undefined);
    const headers: Record<string, string>[] = [];

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, init, handlers) => {
      headers.push(Object.fromEntries(new Headers(init.headers).entries()));
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "instance-restarted", latestEventId: null }),
      });
      throw new Error("disconnect");
    });
    vi.spyOn(sseClient, "computeReconnectDelayMs").mockReturnValue(10);

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    expect(sessionStorage.getItem("fleet-sse-resync-pending")).toBe("1");
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBeNull();
  });

  it("Cutover_64_bit_se_persiste_exactamente", async () => {
    const largeId = "9007199254740993";
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: largeId }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe(largeId));
  });

  it("Caida_despues_del_resync_reconecta_desde_el_cutover", async () => {
    const onStreamReset = vi.fn(async () => undefined);
    const headers: Record<string, string>[] = [];
    let connection = 0;

    vi.spyOn(sseClient, "computeReconnectDelayMs").mockReturnValue(10);
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, init, handlers) => {
      connection += 1;
      headers.push({ ...(init.headers as Record<string, string>) });

      if (connection === 1) {
        await handlers.onEvent({
          event: REALTIME_EVENTS.streamReset,
          data: JSON.stringify({ reason: "replay-gap", latestEventId: "200" }),
        });
        throw new Error("disconnect before live");
      }

      return undefined;
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("200"));
    await waitFor(() => expect(connection).toBeGreaterThanOrEqual(2));
    const reconnectHeaders = headers.find(
      (entry) => entry["Last-Event-ID"] === "200" || entry["last-event-id"] === "200",
    );
    expect(reconnectHeaders).toBeTruthy();
  });

  it("Eventos_entre_resync_y_reconexion_no_se_pierden", async () => {
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "300" }),
      });
      await handlers.onEvent({
        event: REALTIME_EVENTS.vehicleUpdate,
        id: "301",
        data: JSON.stringify({
          vehicleId: "VH-GAP",
          name: "VH-GAP",
          status: "online",
          lastSeenAt: "2026-07-10T10:00:00Z",
          lastSpeedKmh: 40,
          lastLatitude: 4.6,
          lastLongitude: -74.0,
        }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("301"));
    expect(sessionStorage.getItem("fleet-sse-last-event-id")).not.toBe("300");
  });

  it("Stream_reset_limpia_id_y_refresca_snapshot", async () => {
    sessionStorage.setItem("fleet-sse-last-event-id", "44");
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "100" }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("100"));
  });

  it("Stream_reset_no_aplica_replay_incompleto", async () => {
    const onFleetUpdate = vi.fn();
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "10" }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onFleetUpdate, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    expect(onFleetUpdate).not.toHaveBeenCalled();
  });

  it("Reconectar_a_otra_replica_con_gap_converge_por_snapshot", async () => {
    const onStreamReset = vi.fn(async () => undefined);

    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      await handlers.onEvent({
        event: REALTIME_EVENTS.streamReset,
        data: JSON.stringify({ reason: "instance-restarted", latestEventId: "200" }),
      });
    });

    renderHook(() => useSseStream({ enabled: true, onStreamReset }));
    await waitFor(() => expect(onStreamReset).toHaveBeenCalled());
    await waitFor(() => expect(sessionStorage.getItem("fleet-sse-last-event-id")).toBe("200"));
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
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "10" }),
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
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "10" }),
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
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "10" }),
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
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "10" }),
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
        data: JSON.stringify({ reason: "replay-gap", latestEventId: "10" }),
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
