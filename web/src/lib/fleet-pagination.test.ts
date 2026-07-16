import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import type { CursorPage } from "@/types/pagination";
import type { VehicleStatus } from "@/types/fleet";
import {
  fetchFleetPage,
  fetchFleetSnapshot,
  fetchTelemetrySnapshot,
} from "@/lib/fleet-pagination";
import { apiClient } from "@/lib/api-client";
import { computeGlobalAnalyticsFromOps } from "@/lib/analytics";
import { testDeviceId, testVehicleName } from "@/test/device-fixtures";

const mockFetch = vi.fn();

vi.mock("@/lib/utils", () => ({
  getApiBaseUrl: () => "http://localhost:5000",
}));

function vehicle(id: string, displayName?: string): Record<string, unknown> {
  return {
    DeviceId: id,
    VehicleName: displayName ?? testVehicleName(Number.parseInt(id.slice(-3), 10) || 1),
    Status: "online",
    LastSeenAt: "2026-07-10T10:00:00Z",
    LastSpeedKmh: 1,
    LastLatitude: 1,
    LastLongitude: 1,
  };
}

describe("fleet pagination client", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", mockFetch);
    vi.stubGlobal("localStorage", {
      getItem: () => null,
      setItem: () => undefined,
      removeItem: () => undefined,
    });
    mockFetch.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("fetchFleetPage_decodifica_CursorPage", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () =>
        ({
          items: [vehicle("00000000-0000-4000-8000-000000000001")],
          nextCursor: "cursor-1",
          hasMore: true,
        }) satisfies CursorPage<Record<string, unknown>>,
    });

    const page = await fetchFleetPage({ pageSize: 1 });
    expect(page.items).toHaveLength(1);
    expect(page.items[0]?.deviceId).toBe("00000000-0000-4000-8000-000000000001");
    expect(page.nextCursor).toBe("cursor-1");
    expect(page.hasMore).toBe(true);
  });

  it("fetchFleetSnapshot_recoge_multiples_paginas", async () => {
    mockFetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          items: [vehicle("00000000-0000-4000-8000-000000000001")],
          nextCursor: "c2",
          hasMore: true,
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          items: [vehicle("00000000-0000-4000-8000-000000000002")],
          nextCursor: null,
          hasMore: false,
        }),
      });

    const snapshot = await fetchFleetSnapshot({ pageSize: 1 });
    expect(snapshot.vehicles.map((v) => v.deviceId)).toEqual(["00000000-0000-4000-8000-000000000001", "00000000-0000-4000-8000-000000000002"]);
    expect(snapshot.partial).toBe(false);
    expect(snapshot.truncated).toBe(false);
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it("Limite_alcanzado_con_hasMore_marca_partial", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [vehicle("00000000-0000-4000-8000-000000000001"), vehicle("00000000-0000-4000-8000-000000000002")],
        nextCursor: "c2",
        hasMore: true,
      }),
    });

    const snapshot = await fetchFleetSnapshot({ pageSize: 2, maxVehicles: 2 });
    expect(snapshot.vehicles).toHaveLength(2);
    expect(snapshot.partial).toBe(true);
    expect(snapshot.truncated).toBe(true);
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it("Limite_alcanzado_sin_hasMore_no_marca_partial", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [vehicle("00000000-0000-4000-8000-000000000001"), vehicle("00000000-0000-4000-8000-000000000002")],
        nextCursor: null,
        hasMore: false,
      }),
    });

    const snapshot = await fetchFleetSnapshot({ pageSize: 2, maxVehicles: 2 });
    expect(snapshot.vehicles).toHaveLength(2);
    expect(snapshot.partial).toBe(false);
    expect(snapshot.truncated).toBe(false);
  });

  it("apiClient_no_descarta_estado_partial", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [vehicle("00000000-0000-4000-8000-000000000001")],
        nextCursor: "c2",
        hasMore: true,
      }),
    });

    const snapshot = await apiClient.fetchFleetLive({ maxVehicles: 1, pageSize: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.truncated).toBe(true);
    expect(snapshot.vehicles).toHaveLength(1);
  });

  it("maxVehicles_menor_que_pageSize_marca_truncated_sin_hasMore", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [vehicle("00000000-0000-4000-8000-000000000001"), vehicle("00000000-0000-4000-8000-000000000002"), vehicle("00000000-0000-4000-8000-000000000003")],
        nextCursor: null,
        hasMore: false,
      }),
    });

    const snapshot = await fetchFleetSnapshot({ pageSize: 5, maxVehicles: 2 });
    expect(snapshot.vehicles).toHaveLength(2);
    expect(snapshot.truncated).toBe(true);
    expect(snapshot.partial).toBe(true);
  });

  it("cursor_repetido_detiene_el_bucle", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        items: [vehicle("00000000-0000-4000-8000-000000000001")],
        nextCursor: "same",
        hasMore: true,
      }),
    });

    const snapshot = await fetchFleetSnapshot({ pageSize: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.error).toContain("Cursor repetido");
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it("limite_maximo_detiene_la_descarga", async () => {
    let counter = 0;
    mockFetch.mockImplementation(async () => {
      counter += 1;
      const id = testDeviceId(counter);
      return {
        ok: true,
        json: async () => ({
          items: [vehicle(id, testVehicleName(counter))],
          nextCursor: counter < 10 ? `c-${counter}` : null,
          hasMore: counter < 10,
        }),
      };
    });

    const snapshot = await fetchFleetSnapshot({ pageSize: 1, maxVehicles: 3 });
    expect(snapshot.vehicles).toHaveLength(3);
    expect(snapshot.partial).toBe(true);
    expect(snapshot.truncated).toBe(true);
    expect(mockFetch).toHaveBeenCalledTimes(3);
  });

  it("AbortSignal_cancela_paginas_restantes", async () => {
    const controller = new AbortController();
    let calls = 0;
    mockFetch.mockImplementation(async (_input: RequestInfo, init?: RequestInit) => {
      calls += 1;
      if (calls > 1 && init?.signal?.aborted) {
        throw new DOMException("Aborted", "AbortError");
      }
      if (calls === 1) {
        controller.abort();
        return {
          ok: true,
          json: async () => ({
            items: [vehicle("00000000-0000-4000-8000-000000000001")],
            nextCursor: "c2",
            hasMore: true,
          }),
        };
      }
      return {
        ok: true,
        json: async () => ({ items: [], nextCursor: null, hasMore: false }),
      };
    });

    const snapshot = await fetchFleetSnapshot({ signal: controller.signal });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.vehicles).toHaveLength(1);
  });

  it("error_en_pagina_posterior_no_entra_en_bucle", async () => {
    mockFetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          items: [vehicle("00000000-0000-4000-8000-000000000001")],
          nextCursor: "c2",
          hasMore: true,
        }),
      })
      .mockResolvedValueOnce({
        ok: false,
        status: 500,
        headers: { get: () => null },
        json: async () => ({ detail: "fallo" }),
      });

    const snapshot = await fetchFleetSnapshot({ pageSize: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.vehicles).toHaveLength(1);
    expect(snapshot.error).toContain("500");
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it("429_en_primera_pagina_de_flota_propaga_ApiError", async () => {
    const { ApiError } = await import("@/lib/http-error");
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 429,
      headers: { get: (name: string) => (name === "Retry-After" ? "30" : null) },
      json: async () => ({ error: "Demasiadas solicitudes" }),
    });

    await expect(fetchFleetSnapshot({ pageSize: 1 })).rejects.toMatchObject({
      name: "ApiError",
      status: 429,
      retryAfterSeconds: 30,
    });
    expect(ApiError).toBeDefined();
  });

  it("error_de_red_en_primera_pagina_propaga_y_mensaje_resuelve_Docker", async () => {
    mockFetch.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    await expect(fetchFleetSnapshot({ pageSize: 1 })).rejects.toBeInstanceOf(TypeError);

    const { resolveFleetFetchError } = await import("@/lib/fleet-fetch-error");
    expect(resolveFleetFetchError(new TypeError("Failed to fetch"))).toContain("puerto 5000");
  });

  it("503_en_primera_pagina_mantiene_codigo_HTTP", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 503,
      headers: { get: () => null },
      json: async () => ({ error: "unavailable" }),
    });

    await expect(fetchFleetSnapshot()).rejects.toMatchObject({ status: 503 });
  });

  it("AbortError_en_pagina_posterior_no_es_error_de_red", async () => {
    mockFetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          items: [vehicle("00000000-0000-4000-8000-000000000001")],
          nextCursor: "c2",
          hasMore: true,
        }),
      })
      .mockRejectedValueOnce(Object.assign(new Error("Aborted"), { name: "AbortError" }));

    const snapshot = await fetchFleetSnapshot({ pageSize: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.vehicles).toHaveLength(1);
    expect(snapshot.error).toBeUndefined();
  });

  it("maxEvents_menor_que_pageSize_marca_truncated_sin_hasMore", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [
          { eventId: "e-1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 },
          { eventId: "e-2", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:01:00Z", latitude: 1, longitude: 1, speedKmh: 20 },
          { eventId: "e-3", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:02:00Z", latitude: 1, longitude: 1, speedKmh: 30 },
        ],
        nextCursor: null,
        hasMore: false,
      }),
    });

    const snapshot = await fetchTelemetrySnapshot("00000000-0000-4000-8000-000000000001", { pageSize: 5, maxEvents: 2 });
    expect(snapshot.events).toHaveLength(2);
    expect(snapshot.truncated).toBe(true);
    expect(snapshot.partial).toBe(true);
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it("maxEvents_igual_al_total_no_marca_truncated", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [
          { eventId: "e-1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 },
          { eventId: "e-2", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:01:00Z", latitude: 1, longitude: 1, speedKmh: 20 },
        ],
        nextCursor: null,
        hasMore: false,
      }),
    });

    const snapshot = await fetchTelemetrySnapshot("00000000-0000-4000-8000-000000000001", { pageSize: 5, maxEvents: 2 });
    expect(snapshot.events).toHaveLength(2);
    expect(snapshot.truncated).toBe(false);
    expect(snapshot.partial).toBe(false);
  });

  it("no_descarta_eventos_sin_marcar_partial", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [
          { eventId: "e-1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 },
          { eventId: "e-2", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:01:00Z", latitude: 1, longitude: 1, speedKmh: 20 },
          { eventId: "e-3", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:02:00Z", latitude: 1, longitude: 1, speedKmh: 30 },
        ],
        nextCursor: null,
        hasMore: false,
      }),
    });

    const snapshot = await fetchTelemetrySnapshot("00000000-0000-4000-8000-000000000001", { pageSize: 5, maxEvents: 2 });
    expect(snapshot.events).toHaveLength(2);
    expect(snapshot.partial).toBe(true);
    expect(snapshot.truncated).toBe(true);
  });

  it("resultado_nunca_supera_maxEvents", async () => {
    let counter = 0;
    mockFetch.mockImplementation(async () => {
      counter += 1;
      return {
        ok: true,
        json: async () => ({
          items: [
            { eventId: `e-${counter}-1`, deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 },
            { eventId: `e-${counter}-2`, deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:01:00Z", latitude: 1, longitude: 1, speedKmh: 20 },
          ],
          nextCursor: counter < 5 ? `c-${counter}` : null,
          hasMore: counter < 5,
        }),
      };
    });

    const snapshot = await fetchTelemetrySnapshot("00000000-0000-4000-8000-000000000001", { pageSize: 2, maxEvents: 3 });
    expect(snapshot.events.length).toBeLessThanOrEqual(3);
    expect(snapshot.events).toHaveLength(3);
  });

  it("Historial_respeta_limite_total", async () => {
    let counter = 0;
    mockFetch.mockImplementation(async () => {
      counter += 1;
      return {
        ok: true,
        json: async () => ({
          items: [{ eventId: `e-${counter}`, deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
          nextCursor: counter < 5 ? `c-${counter}` : null,
          hasMore: counter < 5,
        }),
      };
    });

    const snapshot = await fetchTelemetrySnapshot("00000000-0000-4000-8000-000000000001", { pageSize: 1, maxEvents: 3 });
    expect(snapshot.events).toHaveLength(3);
    expect(snapshot.truncated).toBe(true);
    expect(snapshot.partial).toBe(true);
    expect(mockFetch).toHaveBeenCalledTimes(3);
  });

  it("Cursor_repetido_en_historial_reporta_estado_parcial", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        items: [{ eventId: "e-1", deviceId: "00000000-0000-4000-8000-000000000001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
        nextCursor: "same",
        hasMore: true,
      }),
    });

    const snapshot = await fetchTelemetrySnapshot("00000000-0000-4000-8000-000000000001", { pageSize: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.error).toContain("Cursor repetido");
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });
});

describe("analytics with truncated snapshot", () => {
  it("Analitica_no_presenta_5000_como_total_si_hay_mas", () => {
    const analytics = computeGlobalAnalyticsFromOps(
      { totalVehicles: 12000, activeVehicles: 8000 },
      5,
      "api",
      { partial: true },
    );

    expect(analytics.totalVehicles).toBe(12000);
    expect(analytics.activeVehicles).toBe(8000);
    expect(analytics.partial).toBe(true);
  });
});
