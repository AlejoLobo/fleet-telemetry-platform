/** @vitest-environment jsdom */
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DashboardHeader } from "@/components/dashboard/dashboard-header";
import {
  MONITOR_REFRESH_RATE_STORAGE_KEY,
  loadMonitorRefreshRate,
  parseMonitorRefreshRate,
  saveMonitorRefreshRate,
} from "@/lib/monitor-refresh-rate";

afterEach(() => {
  cleanup();
});

function getRefreshSelect(): HTMLSelectElement {
  return document.getElementById("monitor-refresh-rate") as HTMLSelectElement;
}

describe("DashboardHeader selector funcional", () => {
  it("muestra todas las opciones y llama onChange", () => {
    const onChange = vi.fn();
    render(
      <DashboardHeader
        loading={false}
        dataSource="api"
        connectionState="connected"
        alertCount={0}
        criticalAlertCount={0}
        refreshRate="realtime"
        onRefreshRateChange={onChange}
        onOpenAlerts={() => undefined}
        onLoadApi={() => undefined}
        onLoadDemo={() => undefined}
        onRefresh={() => undefined}
      />,
    );

    const select = getRefreshSelect();
    expect(select).toBeTruthy();
    expect(select.options).toHaveLength(5);
    expect([...select.options].map((o) => o.text)).toEqual([
      "Tiempo real",
      "Cada 5 segundos",
      "Cada 10 segundos",
      "Cada 30 segundos",
      "Cada 1 minuto",
    ]);

    fireEvent.change(select, { target: { value: "10" } });
    expect(onChange).toHaveBeenCalledWith(10);
  });

  it("se deshabilita hasta que la preferencia esté lista", () => {
    render(
      <DashboardHeader
        loading={false}
        dataSource="api"
        connectionState="connected"
        alertCount={0}
        criticalAlertCount={0}
        refreshRate="realtime"
        refreshRateReady={false}
        onRefreshRateChange={() => undefined}
        onOpenAlerts={() => undefined}
        onLoadApi={() => undefined}
        onLoadDemo={() => undefined}
        onRefresh={() => undefined}
      />,
    );
    expect(getRefreshSelect().disabled).toBe(true);
  });
});

describe("monitor-refresh-rate persistencia", () => {
  it("restaura desde localStorage y valida inválidos", () => {
    window.localStorage.clear();
    expect(loadMonitorRefreshRate()).toBe("realtime");
    saveMonitorRefreshRate(30);
    expect(window.localStorage.getItem(MONITOR_REFRESH_RATE_STORAGE_KEY)).toBe("30");
    expect(loadMonitorRefreshRate()).toBe(30);
    expect(parseMonitorRefreshRate("nope")).toBe("realtime");
  });
});
