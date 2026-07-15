/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import {
  DEMO_REALTIME_REFRESH_MS,
  MONITOR_REFRESH_RATE_OPTIONS,
  MONITOR_REFRESH_RATE_STORAGE_KEY,
  REALTIME_SELECTED_TELEMETRY_MS,
  loadMonitorRefreshRate,
  monitorRefreshRateToMs,
  parseMonitorRefreshRate,
  saveMonitorRefreshRate,
} from "@/lib/monitor-refresh-rate";
import {
  bufferPendingVehicleUpdates,
  takePendingVehicleUpdates,
} from "@/lib/pending-vehicle-updates";
import type { VehicleStatus } from "@/types/fleet";

function vehicle(
  deviceId: string,
  overrides: Partial<VehicleStatus> = {},
): VehicleStatus {
  return {
    deviceId,
    vehicleName: deviceId,
    status: "online",
    lastSeenAt: "2026-07-15T12:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74.0,
    ...overrides,
  };
}

describe("monitor-refresh-rate", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  afterEach(() => {
    window.localStorage.clear();
  });

  it('valor predeterminado es "realtime"', () => {
    expect(loadMonitorRefreshRate()).toBe("realtime");
    expect(parseMonitorRefreshRate(undefined)).toBe("realtime");
    expect(parseMonitorRefreshRate(null)).toBe("realtime");
  });

  it("restaura desde localStorage", () => {
    window.localStorage.setItem(MONITOR_REFRESH_RATE_STORAGE_KEY, "30");
    expect(loadMonitorRefreshRate()).toBe(30);
    window.localStorage.setItem(MONITOR_REFRESH_RATE_STORAGE_KEY, "realtime");
    expect(loadMonitorRefreshRate()).toBe("realtime");
  });

  it('valor inválido vuelve a "realtime"', () => {
    expect(parseMonitorRefreshRate("7")).toBe("realtime");
    expect(parseMonitorRefreshRate(15)).toBe("realtime");
    expect(parseMonitorRefreshRate({})).toBe("realtime");
    window.localStorage.setItem(MONITOR_REFRESH_RATE_STORAGE_KEY, "bogus");
    expect(loadMonitorRefreshRate()).toBe("realtime");
  });

  it("cambiar selector persiste el valor", () => {
    saveMonitorRefreshRate(10);
    expect(window.localStorage.getItem(MONITOR_REFRESH_RATE_STORAGE_KEY)).toBe("10");
    expect(loadMonitorRefreshRate()).toBe(10);
    saveMonitorRefreshRate(60);
    expect(loadMonitorRefreshRate()).toBe(60);
  });

  it("monitorRefreshRateToMs mapea intervalos correctos", () => {
    expect(monitorRefreshRateToMs("realtime")).toBeNull();
    expect(monitorRefreshRateToMs(5)).toBe(5_000);
    expect(monitorRefreshRateToMs(10)).toBe(10_000);
    expect(monitorRefreshRateToMs(30)).toBe(30_000);
    expect(monitorRefreshRateToMs(60)).toBe(60_000);
  });

  it("constantes de refresco realtime usan 5 segundos", () => {
    expect(REALTIME_SELECTED_TELEMETRY_MS).toBe(5_000);
    expect(DEMO_REALTIME_REFRESH_MS).toBe(5_000);
  });

  it("opciones visibles incluyen todas las tasas", () => {
    expect(MONITOR_REFRESH_RATE_OPTIONS.map((o) => o.value)).toEqual([
      "realtime",
      5,
      10,
      30,
      60,
    ]);
    expect(MONITOR_REFRESH_RATE_OPTIONS.map((o) => o.label)).toEqual([
      "Tiempo real",
      "Cada 5 segundos",
      "Cada 10 segundos",
      "Cada 30 segundos",
      "Cada 1 minuto",
    ]);
  });
});

describe("pending-vehicle-updates buffer", () => {
  it("varias actualizaciones del mismo dispositivo conservan solo la última", () => {
    const pending = new Map<string, VehicleStatus>();
    bufferPendingVehicleUpdates(pending, [
      vehicle("d1", { lastSpeedKmh: 10 }),
      vehicle("d1", { lastSpeedKmh: 55 }),
    ]);
    expect(pending.size).toBe(1);
    expect(pending.get("d1")?.lastSpeedKmh).toBe(55);
  });

  it("dispositivos diferentes se conservan en el buffer", () => {
    const pending = new Map<string, VehicleStatus>();
    bufferPendingVehicleUpdates(pending, [
      vehicle("d1"),
      vehicle("d2"),
    ]);
    expect(pending.size).toBe(2);
    expect([...pending.keys()].sort()).toEqual(["d1", "d2"]);
  });

  it("takePendingVehicleUpdates vacía el buffer", () => {
    const pending = new Map<string, VehicleStatus>();
    bufferPendingVehicleUpdates(pending, [vehicle("d1"), vehicle("d2")]);
    const taken = takePendingVehicleUpdates(pending);
    expect(taken).toHaveLength(2);
    expect(pending.size).toBe(0);
    expect(takePendingVehicleUpdates(pending)).toEqual([]);
  });
});

describe("dashboard header selector", () => {
  it("el selector muestra todas las opciones y aria-label", () => {
    const header = readFileSync(
      resolve(process.cwd(), "src/components/dashboard/dashboard-header.tsx"),
      "utf8",
    );
    expect(header).toContain('id="monitor-refresh-rate"');
    expect(header).toContain("MONITOR_REFRESH_RATE_OPTIONS");
    expect(header).toContain("Actualizar");
    expect(header).toContain("onRefreshRateChange");

    expect(MONITOR_REFRESH_RATE_OPTIONS.map((o) => o.label)).toEqual([
      "Tiempo real",
      "Cada 5 segundos",
      "Cada 10 segundos",
      "Cada 30 segundos",
      "Cada 1 minuto",
    ]);
  });
});

describe("page.tsx comportamiento del selector", () => {
  const pageSource = () =>
    readFileSync(resolve(process.cwd(), "src/app/page.tsx"), "utf8");

  it("realtime aplica SSE inmediatamente vía refreshRateRef", () => {
    const source = pageSource();
    expect(source).toMatch(/refreshRateRef\.current === "realtime"/);
    expect(source).toMatch(/applyVehicleUpdates\(updates\)/);
  });

  it("intervalo acumula en buffer y no aplica SSE de inmediato", () => {
    const source = pageSource();
    expect(source).toContain("bufferPendingVehicleUpdates");
    expect(source).toContain("flushPendingVehicleUpdates");
  });

  it("timer de intervalo hace flush + refreshSelectedTelemetry", () => {
    const source = pageSource();
    expect(source).toMatch(
      /flushPendingVehicleUpdatesRef\.current\(\);\s*void refreshSelectedTelemetryRef\.current\(\)/,
    );
  });

  it("10, 30 y 60 segundos usan monitorRefreshRateToMs", () => {
    const source = pageSource();
    expect(source).toContain("monitorRefreshRateToMs(refreshRate)");
  });

  it("cambiar a realtime vacía el buffer", () => {
    const source = pageSource();
    expect(source).toMatch(
      /if \(rate === "realtime"\) \{\s*flushPendingVehicleUpdates\(\);/,
    );
  });

  it("stream-reset limpia buffer y resync inmediato", () => {
    const source = pageSource();
    expect(source).toMatch(
      /onStreamReset:[\s\S]*pendingVehicleUpdatesRef\.current\.clear\(\)/,
    );
    expect(source).toContain("refreshForResync");
  });

  it("botón manual aplica buffer inmediatamente", () => {
    const source = pageSource();
    expect(source).toMatch(
      /handleManualRefresh[\s\S]*flushPendingVehicleUpdates\(\);[\s\S]*await refresh\(\)/,
    );
  });

  it("selector no crea polling REST de toda la flota", () => {
    const source = pageSource();
    expect(source).not.toMatch(/setInterval\([\s\S]{0,220}loadFromApi/);
    expect(source).not.toMatch(/setInterval\([\s\S]{0,220}fetchFleetLive/);
    expect(source).not.toMatch(/setInterval\([\s\S]{0,220}fetchAlertsLive/);
    // refresh() completo solo en handleManualRefresh / acknowledge, no en el interval
    const intervalBlocks = source.match(/setInterval\([^)]+\)/g) ?? [];
    for (const block of intervalBlocks) {
      expect(block).not.toContain("loadFromApi");
      expect(block).not.toContain("fetchFleetLive");
    }
  });

  it("solo se refresca la telemetría del vehículo seleccionado en intervalo API", () => {
    const source = pageSource();
    expect(source).toContain("refreshSelectedTelemetry");
    expect(source).toContain("REALTIME_SELECTED_TELEMETRY_MS");
  });

  it("timer de conectividad permanece independiente", () => {
    const source = pageSource();
    expect(source).toContain("CONNECTIVITY_TICK_MS = 5_000");
    expect(source).toMatch(/setInterval\(\s*\(\)\s*=>\s*setConnectivityNowMs/);
  });

  it("modo Demo respeta la tasa seleccionada", () => {
    const source = pageSource();
    expect(source).toMatch(/dataSource === "demo"/);
    expect(source).toContain("DEMO_REALTIME_REFRESH_MS");
    expect(source).toMatch(/void loadDemoDataRef\.current\(\)/);
  });

  it("persistencia usa localStorage fleet-monitor-refresh-rate", () => {
    const source = pageSource();
    expect(source).toContain("loadMonitorRefreshRate");
    expect(source).toContain("saveMonitorRefreshRate");
  });

  it("alerts se muestran inmediatamente sin buffer", () => {
    const source = pageSource();
    expect(source).toMatch(/onAlert:[\s\S]*setLiveAlerts/);
    expect(source).not.toMatch(/bufferPending.*[Aa]lert/);
  });

  it("inicializa realtime y restaura preferencia tras hidratación", () => {
    const source = pageSource();
    expect(source).toMatch(/useState<MonitorRefreshRate>\("realtime"\)/);
    expect(source).toContain("setRefreshRateReady(true)");
    expect(source).toContain("if (!refreshRateReady) return");
    expect(source).toContain("displayGlobalAnalytics");
  });
});

describe("buffer + flush con fake timers (ciclo visual)", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("5 segundos no aplica antes del timer y aplica al cumplirse", () => {
    const pending = new Map<string, VehicleStatus>();
    const applied: VehicleStatus[][] = [];

    const flush = () => {
      const batch = takePendingVehicleUpdates(pending);
      if (batch.length > 0) applied.push(batch);
    };

    bufferPendingVehicleUpdates(pending, [vehicle("d1", { lastSpeedKmh: 1 })]);
    expect(applied).toHaveLength(0);

    const timer = window.setInterval(flush, 5_000);
    vi.advanceTimersByTime(4_999);
    expect(applied).toHaveLength(0);

    bufferPendingVehicleUpdates(pending, [vehicle("d1", { lastSpeedKmh: 99 })]);
    vi.advanceTimersByTime(1);
    expect(applied).toHaveLength(1);
    expect(applied[0][0].lastSpeedKmh).toBe(99);
    expect(pending.size).toBe(0);

    window.clearInterval(timer);
  });

  it("10, 30 y 60 segundos usan el intervalo correcto", () => {
    for (const seconds of [10, 30, 60] as const) {
      const pending = new Map<string, VehicleStatus>();
      let flushes = 0;
      const timer = window.setInterval(() => {
        takePendingVehicleUpdates(pending);
        flushes += 1;
      }, seconds * 1000);

      bufferPendingVehicleUpdates(pending, [vehicle("d1")]);
      vi.advanceTimersByTime(seconds * 1000 - 1);
      expect(flushes).toBe(0);
      vi.advanceTimersByTime(1);
      expect(flushes).toBe(1);
      window.clearInterval(timer);
    }
  });

  it("cambiar entre intervalos no pierde datos pendientes", () => {
    const pending = new Map<string, VehicleStatus>();
    bufferPendingVehicleUpdates(pending, [vehicle("d1", { lastSpeedKmh: 12 })]);
    // Simula cambio de tasa: se limpia el timer anterior pero el Map permanece.
    expect(pending.get("d1")?.lastSpeedKmh).toBe(12);
    const taken = takePendingVehicleUpdates(pending);
    expect(taken[0].lastSpeedKmh).toBe(12);
  });

  it("desmontar limpia timers (Strict Mode)", () => {
    const spy = vi.spyOn(window, "clearInterval");
    const timer = window.setInterval(() => undefined, 5_000);
    window.clearInterval(timer);
    expect(spy).toHaveBeenCalledWith(timer);
    spy.mockRestore();

    // page.tsx usa return () => window.clearInterval(timer)
    const source = readFileSync(resolve(process.cwd(), "src/app/page.tsx"), "utf8");
    expect(source).toMatch(/return \(\) => window\.clearInterval\(timer\)/);
  });
});
