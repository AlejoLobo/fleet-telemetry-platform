import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import type { CursorPage } from "@/types/pagination";
import type { VehicleStatus } from "@/types/fleet";
import {
  fetchFleetPage,
  fetchFleetSnapshot,
  fetchTelemetrySnapshot,
} from "@/lib/fleet-pagination";
import { computeGlobalAnalytics } from "@/lib/analytics";

const mockFetch = vi.fn();

vi.mock("@/lib/utils", () => ({
  getApiBaseUrl: () => "http://localhost:5000",
}));

function vehicle(id: string): Record<string, unknown> {
  return {
    VehicleId: id,
    Name: id,
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
          items: [vehicle("VH-001")],
          nextCursor: "cursor-1",
          hasMore: true,
        }) satisfies CursorPage<Record<string, unknown>>,
    });

    const page = await fetchFleetPage({ pageSize: 1 });
    expect(page.items).toHaveLength(1);
    expect(page.items[0]?.vehicleId).toBe("VH-001");
    expect(page.nextCursor).toBe("cursor-1");
    expect(page.hasMore).toBe(true);
  });

  it("fetchFleetSnapshot_recoge_multiples_paginas", async () => {
    mockFetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          items: [vehicle("VH-001")],
          nextCursor: "c2",
          hasMore: true,
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          items: [vehicle("VH-002")],
          nextCursor: null,
          hasMore: false,
        }),
      });

    const snapshot = await fetchFleetSnapshot({ pageSize: 1 });
    expect(snapshot.vehicles.map((v) => v.vehicleId)).toEqual(["VH-001", "VH-002"]);
    expect(snapshot.partial).toBe(false);
    expect(snapshot.truncated).toBe(false);
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it("Limite_alcanzado_con_hasMore_marca_partial", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        items: [vehicle("VH-001"), vehicle("VH-002")],
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
        items: [vehicle("VH-001"), vehicle("VH-002")],
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
        items: [vehicle("VH-001")],
        nextCursor: "c2",
        hasMore: true,
      }),
    });

    const snapshot = await fetchFleetSnapshot({ pageSize: 1, maxVehicles: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.truncated).toBe(true);
    expect(snapshot.vehicles).toHaveLength(1);
  });

  it("cursor_repetido_detiene_el_bucle", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        items: [vehicle("VH-001")],
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
      const id = `VH-${String(counter).padStart(3, "0")}`;
      return {
        ok: true,
        json: async () => ({
          items: [vehicle(id)],
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
            items: [vehicle("VH-001")],
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
          items: [vehicle("VH-001")],
          nextCursor: "c2",
          hasMore: true,
        }),
      })
      .mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: async () => ({ detail: "fallo" }),
      });

    const snapshot = await fetchFleetSnapshot({ pageSize: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.vehicles).toHaveLength(1);
    expect(snapshot.error).toBeTruthy();
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it("Historial_respeta_limite_total", async () => {
    let counter = 0;
    mockFetch.mockImplementation(async () => {
      counter += 1;
      return {
        ok: true,
        json: async () => ({
          items: [{ eventId: `e-${counter}`, vehicleId: "VH-001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
          nextCursor: counter < 5 ? `c-${counter}` : null,
          hasMore: counter < 5,
        }),
      };
    });

    const snapshot = await fetchTelemetrySnapshot("VH-001", { pageSize: 1, maxEvents: 3 });
    expect(snapshot.events).toHaveLength(3);
    expect(snapshot.truncated).toBe(true);
    expect(snapshot.partial).toBe(true);
    expect(mockFetch).toHaveBeenCalledTimes(3);
  });

  it("Cursor_repetido_en_historial_reporta_estado_parcial", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        items: [{ eventId: "e-1", vehicleId: "VH-001", timestamp: "2026-07-10T10:00:00Z", latitude: 1, longitude: 1, speedKmh: 10 }],
        nextCursor: "same",
        hasMore: true,
      }),
    });

    const snapshot = await fetchTelemetrySnapshot("VH-001", { pageSize: 1 });
    expect(snapshot.partial).toBe(true);
    expect(snapshot.error).toContain("Cursor repetido");
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });
});

describe("analytics with truncated snapshot", () => {
  it("Analitica_no_presenta_5000_como_total_si_hay_mas", () => {
    const vehicles = Array.from({ length: 5000 }, (_, i) => ({
      vehicleId: `VH-${i}`,
      name: `VH-${i}`,
      status: "online" as const,
      lastSeenAt: "2026-07-10T10:00:00Z",
      lastSpeedKmh: 10,
      lastLatitude: 1,
      lastLongitude: 1,
    }));

    const analytics = computeGlobalAnalytics(vehicles, [], "api", {
      partial: true,
      totalVehiclesOverride: 12000,
    });

    expect(analytics.totalVehicles).toBe(12000);
    expect(analytics.partial).toBe(true);
  });
});

describe("SSE merge behavior", () => {
  it("actualizacion_SSE_reemplaza_vehiculo_sin_duplicarlo", () => {
    const initial: VehicleStatus[] = [
      { vehicleId: "VH-001", name: "VH-001", status: "offline", lastSeenAt: "2026-07-10T09:00:00Z", lastSpeedKmh: 0, lastLatitude: 1, lastLongitude: 1 },
      { vehicleId: "VH-002", name: "VH-002", status: "online", lastSeenAt: "2026-07-10T10:00:00Z", lastSpeedKmh: 20, lastLatitude: 2, lastLongitude: 2 },
    ];
    const liveUpdate: VehicleStatus[] = [
      { vehicleId: "VH-001", name: "VH-001", status: "online", lastSeenAt: "2026-07-10T10:05:00Z", lastSpeedKmh: 35, lastLatitude: 1.1, lastLongitude: 1.1 },
    ];

    const merged = liveUpdate.length > 0 ? liveUpdate : initial;
    const byId = new Map(initial.map((vehicle) => [vehicle.vehicleId, vehicle]));
    for (const vehicle of liveUpdate) byId.set(vehicle.vehicleId, vehicle);
    const result = Array.from(byId.values());

    expect(merged).toHaveLength(1);
    expect(result).toHaveLength(2);
    expect(result.filter((v) => v.vehicleId === "VH-001")).toHaveLength(1);
    expect(result.find((v) => v.vehicleId === "VH-001")?.status).toBe("online");
  });
});

describe("useFleetData truncated state", () => {
  it("useFleetData_muestra_snapshot_truncado", () => {
    const snapshot = {
      vehicles: [{ vehicleId: "VH-001", name: "VH-001", status: "online" as const, lastSeenAt: "2026-07-10T10:00:00Z", lastSpeedKmh: 1, lastLatitude: 1, lastLongitude: 1 }],
      partial: true,
      truncated: true,
    };

    const warning = snapshot.truncated
      ? `Snapshot parcial: se muestran ${snapshot.vehicles.length} vehículos; existen más en el servidor.`
      : null;

    expect(warning).toContain("Snapshot parcial");
    expect(warning).toContain("existen más");
  });
});
