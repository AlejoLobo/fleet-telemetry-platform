/** @vitest-environment jsdom */
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DashboardHeader } from "@/components/dashboard/dashboard-header";

afterEach(() => cleanup());

function getRefreshSelect(): HTMLSelectElement {
  return document.getElementById("monitor-refresh-rate") as HTMLSelectElement;
}

describe("DashboardHeader selector 5/10/15/20", () => {
  it("muestra exactamente cuatro opciones y llama onChange", () => {
    const onChange = vi.fn();
    render(
      <DashboardHeader
        loading={false}
        dataSource="api"
        connectionState="connected"
        alertCount={0}
        criticalAlertCount={0}
        refreshRate={5}
        onRefreshRateChange={onChange}
        onOpenAlerts={() => undefined}
        onLoadApi={() => undefined}
        onLoadDemo={() => undefined}
        onRefresh={() => undefined}
      />,
    );

    const select = getRefreshSelect();
    expect(select.options).toHaveLength(4);
    expect([...select.options].map((o) => o.text)).toEqual([
      "Cada 5 segundos",
      "Cada 10 segundos",
      "Cada 15 segundos",
      "Cada 20 segundos",
    ]);
    fireEvent.change(select, { target: { value: "15" } });
    expect(onChange).toHaveBeenCalledWith(15);
  });

  it("se deshabilita hasta que la preferencia esté lista", () => {
    render(
      <DashboardHeader
        loading={false}
        dataSource="api"
        connectionState="connected"
        alertCount={0}
        criticalAlertCount={0}
        refreshRate={5}
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
