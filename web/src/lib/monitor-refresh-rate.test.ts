/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import {
  DEFAULT_MONITOR_REFRESH_RATE,
  MONITOR_REFRESH_RATE_OPTIONS,
  MONITOR_REFRESH_RATE_STORAGE_KEY,
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

function vehicle(id: string, patch: Partial<VehicleStatus> = {}): VehicleStatus {
  return {
    deviceId: id,
    vehicleName: `VH-${id.slice(-3)}`,
    vehicleType: "car",
    status: "online",
    lastSeenAt: "2026-07-15T12:00:00Z",
    lastSpeedKmh: 40,
    lastLatitude: 4.6,
    lastLongitude: -74,
    ...patch,
  };
}

describe("monitor-refresh-rate 5/10/15/20", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("ofrece exactamente cuatro opciones", () => {
    expect(MONITOR_REFRESH_RATE_OPTIONS.map((o) => o.value)).toEqual([5, 10, 15, 20]);
    expect(MONITOR_REFRESH_RATE_OPTIONS.map((o) => o.label)).toEqual([
      "Cada 5 segundos",
      "Cada 10 segundos",
      "Cada 15 segundos",
      "Cada 20 segundos",
    ]);
  });

  it("valor predeterminado es 5", () => {
    expect(DEFAULT_MONITOR_REFRESH_RATE).toBe(5);
    expect(loadMonitorRefreshRate()).toBe(5);
    expect(parseMonitorRefreshRate(undefined)).toBe(5);
  });

  it("migra valores legados realtime/30/60 a 5", () => {
    expect(parseMonitorRefreshRate("realtime")).toBe(5);
    expect(parseMonitorRefreshRate(30)).toBe(5);
    expect(parseMonitorRefreshRate(60)).toBe(5);
    expect(parseMonitorRefreshRate("30")).toBe(5);
  });

  it("al restaurar reescribe localStorage normalizado", () => {
    for (const legacy of ["realtime", "30", "60", "bogus"]) {
      window.localStorage.setItem(MONITOR_REFRESH_RATE_STORAGE_KEY, legacy);
      const restored = loadMonitorRefreshRate();
      expect(restored).toBe(5);
      saveMonitorRefreshRate(restored);
      expect(window.localStorage.getItem(MONITOR_REFRESH_RATE_STORAGE_KEY)).toBe("5");
    }
  });

  it("restaura 5/10/15/20", () => {
    for (const rate of [5, 10, 15, 20] as const) {
      saveMonitorRefreshRate(rate);
      expect(loadMonitorRefreshRate()).toBe(rate);
      expect(window.localStorage.getItem(MONITOR_REFRESH_RATE_STORAGE_KEY)).toBe(String(rate));
    }
  });

  it("monitorRefreshRateToMs mapea intervalos", () => {
    expect(monitorRefreshRateToMs(5)).toBe(5_000);
    expect(monitorRefreshRateToMs(10)).toBe(10_000);
    expect(monitorRefreshRateToMs(15)).toBe(15_000);
    expect(monitorRefreshRateToMs(20)).toBe(20_000);
  });
});

describe("page.tsx hidratación y buffer", () => {
  const pageSource = () =>
    readFileSync(resolve(process.cwd(), "src/app/page.tsx"), "utf8");

  it("inicializa en 5 y restaura tras montaje", () => {
    const source = pageSource();
    expect(source).toContain("DEFAULT_MONITOR_REFRESH_RATE");
    expect(source).toContain("setRefreshRateReady(true)");
    expect(source).toContain("if (!refreshRateReady) return");
    expect(source).toContain("saveMonitorRefreshRate(restored)");
    expect(source).not.toContain('"realtime"');
  });

  it("no aplica buffer antes del timer y aplica al cumplirse", () => {
    vi.useFakeTimers();
    const pending = new Map<string, VehicleStatus>();
    const applied: VehicleStatus[][] = [];
    const flush = () => {
      const batch = takePendingVehicleUpdates(pending);
      if (batch.length) applied.push(batch);
    };
    bufferPendingVehicleUpdates(pending, [vehicle("d1", { lastSpeedKmh: 1 })]);
    const timer = window.setInterval(flush, 5_000);
    vi.advanceTimersByTime(4_999);
    expect(applied).toHaveLength(0);
    vi.advanceTimersByTime(1);
    expect(applied).toHaveLength(1);
    expect(applied[0][0].lastSpeedKmh).toBe(1);
    window.clearInterval(timer);
    vi.useRealTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });
});
